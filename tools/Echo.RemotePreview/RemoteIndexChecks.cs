using System.Net;
using System.Text;
using Echo.Application.Remote;
using Echo.Contracts.Library;
using Echo.Contracts.Remote;
using Echo.Infrastructure.Library;
using Echo.Playback;

internal static class RemoteIndexChecks
{
    public static void Run(string root)
    {
        var checks = 0;
        RejectPublic(); checks++;
        RemoteSourceCatalog.Validate(new RemoteConnection("lan", "LAN", "http://192.168.1.20/dav/", "user", "secret")); checks++;
        CheckDigest(); checks++;
        checks += CheckIndex(root);
        checks += CheckSubsonic(root);
        Console.WriteLine($"PASS {checks} remote index checks; library rows, retirement, playback identity and digest retry. No live accounts used.");
    }
    private static void RejectPublic()
    {
        try { RemoteSourceCatalog.Validate(new RemoteConnection("pub", "Public", "http://music.example.com/dav/", "user", "secret")); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Public HTTP credentials must be rejected");
    }
    private static void CheckDigest()
    {
        var handler = new DigestHandler();
        using var webdav = new Echo.Infrastructure.Remote.WebDavFileClient(handler);
        var source = new RemoteConnection("dav", "NAS", "https://nas.example/dav/", "Mufasa", "Circle Of Life", SyncMode: "index");
        var page = webdav.BrowseAsync(source, "/", 0, "", false, CancellationToken.None).GetAwaiter().GetResult();
        if (page.Entries.Count != 1 || !handler.DigestAccepted) throw new InvalidOperationException("Digest challenge was not retried");
        var media = webdav.ResolveMediaAsync(source, page.Entries[0], CancellationToken.None).GetAwaiter().GetResult();
        if (!media.Uri.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) || media.Headers?.ContainsKey("authorization") == true)
            throw new InvalidOperationException("Digest playback must stay on the loopback proxy");
    }
    private static int CheckIndex(string root)
    {
        var directory = Path.Combine(root, "index-check"); Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, "library.sqlite");
        var files = new IndexTree();
        var catalog = new RemoteSourceCatalog(null, files);
        var source = new RemoteConnection("src", "NAS", "https://music.example.com/dav/", "user", "secret", SyncMode: "index");
        catalog.SaveAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        var database = LibraryDatabase.OpenAsync(databasePath).GetAwaiter().GetResult();
        try
        {
        var sync = new RemoteLibrarySync(catalog, files, database);
        var first = sync.RunAsync("src", null, CancellationToken.None).GetAwaiter().GetResult();
        if (first.Inserted != 2 || first.Missing != 0) throw new InvalidOperationException("Index did not write both tracks");
        var tracks = database.GetTracksAsync(new TrackQuery(20, Filter: new LibraryFilter(Source: LibrarySource.Remote))).GetAwaiter().GetResult();
        if (tracks.Items.Count != 2 || tracks.Items.Any(x => x.Source?.Kind != "remote")) throw new InvalidOperationException("Library query did not return remote tracks");
        var albums = database.GetAlbumsAsync(new AlbumQuery()).GetAwaiter().GetResult();
        var artists = database.GetArtistsAsync(new ArtistQuery()).GetAwaiter().GetResult();
        if (albums.Items.Count == 0 || artists.Items.Count == 0) throw new InvalidOperationException("Remote tracks did not enter albums or artists");
        var present = database.PresentAsync("src", ["/Album/a.mp3", "/missing.mp3"], CancellationToken.None).GetAwaiter().GetResult();
        if (present.Count != 1 || present[0] != "/Album/a.mp3") throw new InvalidOperationException("Indexed path lookup failed");
        files.DropLoose = true;
        var second = sync.RunAsync("src", null, CancellationToken.None).GetAwaiter().GetResult();
        if (second.Unchanged != 1 || second.Missing != 1) throw new InvalidOperationException("Unseen remote track was not retired");
        var empty = new RemoteConnection("empty", "Sub", "https://music.example.com/rest/", "user", "secret", "subsonic", SyncMode: "index");
        catalog.SaveAsync(empty, CancellationToken.None).GetAwaiter().GetResult();
        var kept = sync.RunAsync("empty", null, CancellationToken.None).GetAwaiter().GetResult();
        if (kept.Inserted != 1) throw new InvalidOperationException("Subsonic track was not indexed");
        files.EmptySubsonic = true;
        var spared = sync.RunAsync("empty", null, CancellationToken.None).GetAwaiter().GetResult();
        if (spared.Missing != 0) throw new InvalidOperationException("Empty Subsonic scan retired the index");
        var still = database.GetTracksAsync(new TrackQuery(20, Filter: new LibraryFilter(Source: LibrarySource.Remote))).GetAwaiter().GetResult();
        if (still.Items.Count != 2) throw new InvalidOperationException("Empty Subsonic scan removed indexed tracks");
        var browse = source with { Id = "browse", SyncMode = "browse" };
        catalog.SaveAsync(browse, CancellationToken.None).GetAwaiter().GetResult();
        try { sync.RunAsync("browse", null, CancellationToken.None).GetAwaiter().GetResult(); throw new InvalidOperationException("Browse mode indexed"); }
        catch (InvalidOperationException error) when (error.Message.Contains("只浏览")) { }
        var resolver = new RemotePlaybackResolver(catalog, files);
        var resolved = resolver.ResolveAsync(new AudioSource("remote", RemoteTrackPaths.Compose("src", "/Album/a.mp3")), CancellationToken.None).GetAwaiter().GetResult();
        if (resolved.Kind != "http" || resolved.Headers?.ContainsKey("authorization") != true || resolved.Uri.Contains("secret"))
            throw new InvalidOperationException("Playback did not resolve a credentialed stream");
        new AudioSource("remote", RemoteTrackPaths.Compose("src", "/Album/a.mp3")).Validate();
        return 8;
        }
        finally { database.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private static int CheckSubsonic(string root)
    {
        var directory = Path.Combine(root, "subsonic-check"); Directory.CreateDirectory(directory);
        var server = new SubsonicHandler();
        using var files = new Echo.Infrastructure.Remote.RemoteFileClients(server);
        var catalog = new RemoteSourceCatalog(null, files);
        var source = new RemoteConnection("sub", "Navidrome", "https://music.example/rest/", "user", "secret", "subsonic", SyncMode: "index");
        catalog.SaveAsync(source, CancellationToken.None).GetAwaiter().GetResult();
        var page = files.BrowseAsync(source, "/", 0, "", false, CancellationToken.None).GetAwaiter().GetResult();
        if (page.Entries.Count != 1 || page.Entries[0].Name != "Music") throw new InvalidOperationException("A single Subsonic music folder was dropped");
        var database = LibraryDatabase.OpenAsync(Path.Combine(directory, "library.sqlite")).GetAwaiter().GetResult();
        try
        {
            var sync = new RemoteLibrarySync(catalog, files, database);
            var first = sync.RunAsync("sub", null, CancellationToken.None).GetAwaiter().GetResult();
            if (first.Inserted != 2 || server.AlbumDetails != 2) throw new InvalidOperationException("Subsonic albums were not indexed from getAlbum");
            var tracks = database.GetTracksAsync(new TrackQuery(20, Filter: new LibraryFilter(Source: LibrarySource.Remote))).GetAwaiter().GetResult();
            var song = tracks.Items.Single(item => item.Title == "Song");
            if (song.Genre != "Jazz" || song.TrackNumber != 3 || song.DiscNumber != 1 || song.SampleRate != 96000 || song.BitDepth != 24
                || song.Source?.AlbumArtist != "Ada Album" || song.DurationSeconds != 120)
                throw new InvalidOperationException("Subsonic album metadata was not kept");
            sync.RunAsync("sub", null, CancellationToken.None).GetAwaiter().GetResult();
            if (server.AlbumDetails != 2) throw new InvalidOperationException("Unchanged Subsonic albums were fetched again");
            server.Incomplete = true;
            var partial = sync.RunAsync("sub", null, CancellationToken.None).GetAwaiter().GetResult();
            if (partial.Missing != 0 || server.AlbumDetails != 4) throw new InvalidOperationException("An incomplete Subsonic album retired indexed songs");
            var kept = database.GetTracksAsync(new TrackQuery(20, Filter: new LibraryFilter(Source: LibrarySource.Remote))).GetAwaiter().GetResult();
            if (kept.Items.Count != 2) throw new InvalidOperationException("Incomplete Subsonic album removed a song");
            server.Empty = true;
            var empty = sync.RunAsync("sub", null, CancellationToken.None).GetAwaiter().GetResult();
            if (empty.Missing != 0) throw new InvalidOperationException("An empty Subsonic album list retired the index");
            return 5;
        }
        finally { database.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
    private sealed class SubsonicHandler : HttpMessageHandler
    {
        public int AlbumDetails; public bool Incomplete; public bool Empty;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            var id = Param(request.RequestUri, "id"); var offset = Param(request.RequestUri, "offset");
            string body;
            if (path.Contains("getMusicFolders", StringComparison.Ordinal)) body = """{"subsonic-response":{"status":"ok","musicFolders":{"musicFolder":{"id":"1","name":"Music"}}}}""";
            else if (path.Contains("getScanStatus", StringComparison.Ordinal)) body = """{"subsonic-response":{"status":"ok","scanStatus":{"scanning":false}}}""";
            else if (path.Contains("getAlbumList2", StringComparison.Ordinal))
                body = Empty || offset is not ("" or "0")
                    ? """{"subsonic-response":{"status":"ok","albumList2":{"album":[]}}}"""
                    : Incomplete
                        ? """{"subsonic-response":{"status":"ok","albumList2":{"album":[{"id":"a","name":"A","artist":"Ada Album","songCount":1,"coverArt":"ca"},{"id":"b","name":"B","artist":"Bea","songCount":9,"coverArt":"cb"}]}}}"""
                        : """{"subsonic-response":{"status":"ok","albumList2":{"album":[{"id":"a","name":"A","artist":"Ada Album","songCount":1,"coverArt":"ca"},{"id":"b","name":"B","artist":"Bea","songCount":1,"coverArt":"cb"}]}}}""";
            else if (path.Contains("getAlbum", StringComparison.Ordinal))
            {
                AlbumDetails++;
                body = Incomplete && id == "b" ? """{"subsonic-response":{"status":"ok"}}""" : Album(id);
            }
            else body = """{"subsonic-response":{"status":"ok"}}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
        private static string Album(string id)
        {
            var name = id.ToUpperInvariant(); var artist = id == "a" ? "Ada Album" : "Bea"; var title = id == "a" ? "Song" : "Other";
            return "{\"subsonic-response\":{\"status\":\"ok\",\"album\":{\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"artist\":\"" + artist
                + "\",\"songCount\":1,\"song\":[{\"id\":\"song-" + id + "\",\"title\":\"" + title + "\",\"artist\":\"Ada\",\"album\":\"" + name
                + "\",\"duration\":120,\"suffix\":\"flac\",\"size\":20,\"created\":\"2020-01-02T00:00:00Z\",\"track\":3,\"discNumber\":1,\"year\":2020,\"genre\":\"Jazz\",\"samplingRate\":96000,\"bitDepth\":24,\"coverArt\":\"c" + id + "\"}]}}}";
        }
        private static string Param(Uri? uri, string key)
        {
            foreach (var part in (uri?.Query ?? "").TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var cut = part.IndexOf('=');
                if (cut > 0 && Uri.UnescapeDataString(part[..cut]) == key) return Uri.UnescapeDataString(part[(cut + 1)..]);
            }
            return "";
        }
    }
    private sealed class IndexTree : IRemoteFileClient
    {
        public bool DropLoose;
        public bool EmptySubsonic;
        public Task<RemoteConnection> TestConnectionAsync(RemoteConnection source, CancellationToken token) => Task.FromResult(source);
        public Task<RemoteDirectoryPage> BrowseAsync(RemoteConnection source, string path, int page, string search, bool audioOnly, CancellationToken token)
            => Task.FromResult(new RemoteDirectoryPage([], 0, page, 200));
        public Task<IReadOnlyList<RemoteEntry>> ListChildrenAsync(RemoteConnection source, string path, CancellationToken token)
        {
            if (source.Id == "empty")
                return Task.FromResult<IReadOnlyList<RemoteEntry>>(EmptySubsonic ? [] : [new("/song.mp3", "song.mp3", false, true, 5, "2020-01-01", Title: "Keep", Artist: "Ada", Album: "Keep")]);
            if (path == "/")
            {
                var rows = new List<RemoteEntry> { new("/Album/", "Album", true, false, null, null) };
                if (!DropLoose) rows.Add(new("/loose.flac", "loose.flac", false, true, 10, "2020-01-01", Title: "Loose", Artist: "Ada", Album: "Singles"));
                return Task.FromResult<IReadOnlyList<RemoteEntry>>(rows);
            }
            return Task.FromResult<IReadOnlyList<RemoteEntry>>([new("/Album/a.mp3", "a.mp3", false, true, 20, "2020-01-02", Title: "Song", Artist: "Ada", Album: "Album", DurationSeconds: 3, Codec: "MP3")]);
        }
        public Task<RemoteMediaSource> ResolveMediaAsync(RemoteConnection source, RemoteEntry entry, CancellationToken token)
            => Task.FromResult(new RemoteMediaSource("http", "https://music.example.com/a.mp3", new Dictionary<string, string> { ["authorization"] = "Basic eA==" }));
    }
    private sealed class DigestHandler : HttpMessageHandler
    {
        public bool DigestAccepted;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.Authorization?.Scheme == "Digest" && (request.Headers.Authorization.Parameter ?? "").Contains("response="))
            {
                DigestAccepted = true;
                var body = """<d:multistatus xmlns:d="DAV:"><d:response><d:href>/dav/song.flac</d:href><d:propstat><d:status>HTTP/1.1 200 OK</d:status><d:prop><d:resourcetype/><d:getcontentlength>12</d:getcontentlength><d:getlastmodified>Thu, 01 Oct 2026 12:00:00 GMT</d:getlastmodified></d:prop></d:propstat></d:response></d:multistatus>""";
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)207) { Content = new StringContent(body, Encoding.UTF8, "application/xml") });
            }
            var challenge = new HttpResponseMessage(HttpStatusCode.Unauthorized);
            challenge.Headers.WwwAuthenticate.Add(new System.Net.Http.Headers.AuthenticationHeaderValue("Digest", "realm=\"testrealm@host.com\", nonce=\"dcd98b7102dd2f0e8b11d0f600bfb0c093\", qop=\"auth\""));
            return Task.FromResult(challenge);
        }
    }
}
