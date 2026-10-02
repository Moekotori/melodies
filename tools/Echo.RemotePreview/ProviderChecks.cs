using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Application.Remote;
using Echo.Contracts.Remote;
using Echo.Infrastructure.Remote;

internal static class ProviderChecks
{
    public static void Run(string root)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12)); var token = deadline.Token; var checks = 0;
        var fixture = new ProviderFixture(); using var files = new RemoteFileClients(fixture, new RemoteFixture());
        var store = new MemoryConnections(); var catalog = new RemoteSourceCatalog(store, files); files.BindTokenWriter(catalog);
        var clientId = "11111111-1111-4111-8111-111111111111";
        Check(RemoteProviders.All.Order().SequenceEqual(new[] { "webdav", "jellyfin", "emby", "smb", "sshfs", "subsonic", "onedrive" }.Order()), "Original seven provider IDs");
        var legacy = JsonSerializer.Deserialize("{\"Id\":\"old\",\"Name\":\"Old\",\"BaseUrl\":\"https://example.com/dav/\"}", ProviderTestJson.Default.RemoteConnection)!;
        Check(legacy.Provider == "webdav", "Existing WebDAV configurations retain provider default");
        foreach (var provider in new[] { "jellyfin", "emby" })
        {
            var source = new RemoteConnection(provider, provider, "https://media.example.com/", "user", "memory-only", provider);
            source = RemoteSourceCatalog.Validate(source);
            var library = Get(files.BrowseAsync(source, "/", 0, "", false, token)).Entries.Single();
            var songs = Get(files.BrowseAsync(source, library.Path, 0, "", true, token));
            Check(library.IsDirectory && songs.Entries.Single().Artist == "Hikaru Utada", provider + " password / library / song metadata");
            var media = Get(files.ResolveMediaAsync(source, songs.Entries[0], token));
            Check(media.Kind == "http" && ProviderFixture.Query(new Uri(media.Uri).Query).ContainsKey("api_key") && media.Headers is { Count: > 0 }, provider + " stream and checkpoint exclusion");
            source = source with { AuthMode = "token", Password = "api-key-memory-only", UserId = "user1" };
            Check(Get(files.BrowseAsync(source, "/", 0, "", false, token)).Entries.Count == 1, provider + " API key authentication");
            Complete(catalog.SaveAsync(source, token));
        }
        var subsonic = new RemoteConnection("sub", "Navidrome", "https://music.example.com/", "user", "memory-only", "subsonic");
        var location = "/"; RemoteEntry? song = null;
        for (var depth = 0; depth < 4; depth++)
        {
            var entry = Get(files.BrowseAsync(subsonic, location, 0, "", false, token)).Entries.Single();
            if (entry.IsAudio) song = entry; else location = entry.Path;
        }
        Check(song?.Artist == "Aimer", "Subsonic folder / artist / album / song browsing");
        var subMedia = Get(files.ResolveMediaAsync(subsonic, song!, token)); var subParams = ProviderFixture.Query(new Uri(subMedia.Uri).Query);
        Check(subParams["format"] == "raw" && subParams["t"].Length == 32 && !subMedia.Uri.Contains("memory-only"), "Subsonic salted stream authentication");
        Check(Get(files.BrowseAsync(subsonic with { AuthMode = "apiKey" }, "/", 0, "", false, token)).Entries.Count == 1, "OpenSubsonic API key mode");
        Complete(catalog.SaveAsync(subsonic, token));
        var mount = Path.Combine(root, "mounted-fixture"); Directory.CreateDirectory(mount); Directory.CreateDirectory(Path.Combine(mount, "Albums"));
        File.WriteAllBytes(Path.Combine(mount, "music.wav"), new byte[44]);
        foreach (var provider in new[] { "smb", "sshfs" })
        {
            var source = RemoteSourceCatalog.Validate(new(provider, provider, mount, Provider: provider));
            var entries = Get(files.BrowseAsync(source, "/", 0, "", true, token)).Entries;
            Check(entries.Count == 2 && entries[0].IsDirectory, provider + " real OS directory browse");
            Check(Get(files.ResolveMediaAsync(source, entries.First(x => x.IsAudio), token)).Kind == "local", provider + " mounted native source");
            Reject(files.BrowseAsync(source, "/../", 0, "", false, token), provider + " traversal rejection");
            Complete(catalog.SaveAsync(source, token));
        }
        var oneDrive = new RemoteConnection("onedrive", "OneDrive", "https://graph.microsoft.com/v1.0/", Provider: "onedrive",
            OneDrive: new("access-memory-only", "refresh-memory-only", clientId, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600));
        var graph = Get(files.BrowseAsync(oneDrive, "/", 0, "", false, token));
        Check(graph.Entries.Count == 2 && graph.Entries.Any(x => x.Name == "星光.flac") && fixture.GraphPages == 2, "OneDrive Graph paging / Unicode");
        var driveMedia = Get(files.ResolveMediaAsync(oneDrive, graph.Entries.First(x => x.IsAudio), token));
        Check(driveMedia.Uri.StartsWith("https://files.example.com/") && !driveMedia.Headers!.ContainsKey("authorization"), "OneDrive signed file URL excludes Graph bearer");
        fixture.BadGraphLink = true; Reject(files.BrowseAsync(oneDrive, "/", 0, "", false, token), "Graph nextLink host isolation"); fixture.BadGraphLink = false;
        fixture.Oversized = true; Reject(files.BrowseAsync(oneDrive, "/", 0, "", false, token), "JSON response size cap"); fixture.Oversized = false;
        // An expired new draft is refreshed without a premature store write; the committed draft contains its new tokens.
        Complete(catalog.SaveAsync(oneDrive with { OneDrive = oneDrive.OneDrive! with { ExpiresUnix = 1 } }, token));
        Check(catalog.GetConnection("onedrive").OneDrive!.RefreshToken == "refresh-memory-only-new", "OneDrive draft refresh commits exact validated token");
        var expired = catalog.GetConnection("onedrive") with { OneDrive = catalog.GetConnection("onedrive").OneDrive! with { ExpiresUnix = 1 } };
        store.Saved = [expired]; var freshOwner = new RemoteSourceCatalog(store, files); files.ForgetSource("onedrive"); files.BindTokenWriter(freshOwner); Complete(freshOwner.LoadAsync(token));
        Get(freshOwner.BrowseAsync("onedrive", "/", 0, "", false, token));
        Check(freshOwner.GetConnection("onedrive").OneDrive!.ExpiresUnix > 1, "OneDrive refresh persists through catalog writer");
        Check(!Get(freshOwner.WriteAuthorizationAsync("onedrive", "stale-refresh", expired.OneDrive!, token)), "Stale refresh cannot overwrite account");
        var playback = Get(RemotePlayback.CreateTrackAsync(freshOwner, files, "onedrive", graph.Entries.First(x => x.IsAudio), token));
        Check(!playback.FilePath.Contains("signature") && playback.Source!.Headers is { Count: > 0 }, "HTTP history identity excludes signed credentials");
        Task? browser;
        browser = null;
        var tokens = Get(files.AuthorizeAsync(clientId, (uri, cancellation) =>
        {
            var query = ProviderFixture.Query(uri.Query); fixture.Challenge = query["code_challenge"];
            Check(query["code_challenge_method"] == "S256" && query["scope"].Contains("Files.Read"), "Browser PKCE request");
            browser = Task.Run(async () =>
            {
                var redirect = new Uri(query["redirect_uri"]);
                Check(await Callback(redirect.Port, "bad-state", cancellation) == 400, "Loopback rejects invalid state");
                Check(await Callback(redirect.Port, query["state"], cancellation) == 200, "Loopback accepts correct state");
            }, cancellation);
            return Task.CompletedTask;
        }, token));
        Complete(browser!);
        Check(tokens.RefreshToken.Length > 0 && fixture.VerifiedPkce, "Actual loopback exchange verifies PKCE");
        using var cancelled = new CancellationTokenSource();
        try { Get(files.AuthorizeAsync(clientId, (_, _) => { cancelled.Cancel(); return Task.CompletedTask; }, cancelled.Token)); throw new InvalidOperationException("Authorization should cancel"); }
        catch (OperationCanceledException) { checks++; }
        Console.WriteLine($"PASS {checks} provider checks; seven routes, bounded protocol fixtures, real mount files and real loopback PKCE. No live accounts used.");
        void Check(bool success, string name) { if (!success) throw new InvalidOperationException(name); checks++; }
        void Complete(Task task) => task.GetAwaiter().GetResult();
        T Get<T>(Task<T> task) => task.GetAwaiter().GetResult();
        void Reject(Task task, string name) { try { Complete(task); } catch (Exception error) when (error is ArgumentException or IOException) { checks++; return; } throw new InvalidOperationException(name); }
    }
    private static async Task<int> Callback(int port, string state, CancellationToken token)
    {
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, port, token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET /?code=code-memory-only&state={Uri.EscapeDataString(state)} HTTP/1.1\r\nHost: localhost\r\nConnection: close\r\n\r\n"), token);
        using var reader = new StreamReader(stream); var line = await reader.ReadLineAsync(token);
        return int.Parse(line!.Split(' ')[1]);
    }
}
[JsonSerializable(typeof(RemoteConnection))]
internal partial class ProviderTestJson : JsonSerializerContext;
