using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

internal sealed partial class SubsonicFileClient
{
    private const int AlbumPageSize = 500;
    public async IAsyncEnumerable<RemoteLibraryPage> ReadLibraryPagesAsync(RemoteConnection source, IRemoteTrackIndex index,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        await WaitForServerAsync(source, token).ConfigureAwait(false);
        var root = ScanRoot(source);
        if (root.Kind is "d" or "a")
        {
            await foreach (var page in WalkDirectoryAsync(source, root.Id, token).ConfigureAwait(false)) yield return page;
            yield break;
        }
        if (root.Kind == "b")
        {
            yield return await LoadAlbumAsync(source, index, new(root.Id, "", "", "", null, null, "", null, ""), token).ConfigureAwait(false);
            yield break;
        }
        for (var offset = 0; offset < 100_000; offset += AlbumPageSize)
        {
            token.ThrowIfCancellationRequested();
            var parameters = new Dictionary<string, string> { ["type"] = "alphabeticalByName", ["size"] = AlbumPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), ["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            if (root.Kind == "f") parameters["musicFolderId"] = root.Id;
            using var json = await http.JsonAsync(Url(source, "getAlbumList2", parameters), token).ConfigureAwait(false);
            var list = Property(Response(json.RootElement), "albumList2");
            if (list.ValueKind != JsonValueKind.Object) throw new IOException("Subsonic 专辑列表不完整，已停止同步，不会把曲库标成缺失。");
            var albums = Many(list, "album").Select(Summary).ToArray();
            if (albums.Any(album => album is null)) throw new IOException("Subsonic 专辑列表不完整，已停止同步，不会把曲库标成缺失。");
            if (albums.Length > AlbumPageSize || offset + albums.Length > 100_000) throw new IOException("Subsonic 专辑超过 100,000 张，请缩小同步范围。");
            for (var indexInPage = 0; indexInPage < albums.Length && !token.IsCancellationRequested; indexInPage += 4)
            {
                var slice = albums.Skip(indexInPage).Take(4).Cast<AlbumSummary>().ToArray();
                var pages = await Task.WhenAll(slice.Select(album => LoadAlbumAsync(source, index, album, token))).ConfigureAwait(false);
                foreach (var page in pages) yield return page;
            }
            if (albums.Length < AlbumPageSize) yield break;
        }
    }
    private async Task WaitForServerAsync(RemoteConnection source, CancellationToken token)
    {
        var giveUp = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            bool scanning;
            try
            {
                using var json = await http.JsonAsync(Url(source, "getScanStatus", new()), token).ConfigureAwait(false);
                var status = Property(Response(json.RootElement), "scanStatus");
                scanning = Property(status, "scanning").ValueKind == JsonValueKind.True;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return; }
            if (!scanning) return;
            if (token.IsCancellationRequested || DateTime.UtcNow.AddSeconds(5) > giveUp)
                throw new IOException("Subsonic / Navidrome 正在扫描媒体库，已等待 90 秒仍未完成，请在服务器扫描完成后重试。");
            await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
        }
    }
    private async Task<RemoteLibraryPage> LoadAlbumAsync(RemoteConnection source, IRemoteTrackIndex index, AlbumSummary album, CancellationToken token)
    {
        var fingerprint = Fingerprint(album);
        var cached = await index.ReadAlbumCacheAsync(source.Id, album.Id, fingerprint, token).ConfigureAwait(false);
        var songs = cached is null ? null : ReadCachedSongs(cached);
        if (songs is null)
        {
            JsonElement? detail = null;
            for (var attempt = 0; attempt < 2 && detail is null; attempt++)
            {
                if (attempt == 1) await Task.Delay(250, token).ConfigureAwait(false);
                try { detail = await ReadAlbumDetailAsync(source, album, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { detail = null; }
            }
            if (detail is not JsonElement albumElement) return new([], true);
            songs = Songs(albumElement).ToArray();
            var payload = WriteSongs(songs);
            if (payload is not null) await index.WriteAlbumCacheAsync(source.Id, album.Id, fingerprint, payload, token).ConfigureAwait(false);
        }
        foreach (var song in songs) RememberCover(song.Id, song.Cover);
        return new(songs.Select(song => song.ToTrack(source.Id)).ToArray(), false);
    }
    private async Task<JsonElement?> ReadAlbumDetailAsync(RemoteConnection source, AlbumSummary album, CancellationToken token)
    {
        using var json = await http.JsonAsync(Url(source, "getAlbum", new() { ["id"] = album.Id }), token).ConfigureAwait(false);
        var detail = Property(Response(json.RootElement), "album");
        if (detail.ValueKind != JsonValueKind.Object || Id(detail).Length == 0) return null;
        var count = RemoteBrowseRules.Number(detail, "songCount") ?? album.SongCount;
        var songs = Many(detail, "song").ToArray();
        if (songs.Any(song => Id(song).Length == 0)) return null;
        if (!detail.TryGetProperty("song", out _) && count is not 0) return null;
        if (count is long expected && expected > songs.Length) return null;
        return detail.Clone();
    }
    private async IAsyncEnumerable<RemoteLibraryPage> WalkDirectoryAsync(RemoteConnection source, string rootId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        var pending = new Queue<string>(); pending.Enqueue(rootId);
        var seen = new HashSet<string>(StringComparer.Ordinal) { rootId };
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            using var json = await http.JsonAsync(Url(source, "getMusicDirectory", new() { ["id"] = pending.Dequeue() }), token).ConfigureAwait(false);
            var directory = Property(Response(json.RootElement), "directory");
            if (directory.ValueKind != JsonValueKind.Object) throw new IOException("Subsonic 目录响应不完整，已停止同步，不会把曲库标成缺失。");
            var tracks = new List<RemoteIndexTrack>();
            foreach (var child in Many(directory, "child"))
            {
                var id = Id(child);
                if (id.Length == 0) continue;
                var isDirectory = child.TryGetProperty("isDir", out var flag) && (flag.ValueKind == JsonValueKind.True || flag.ValueKind == JsonValueKind.String && flag.GetString() == "true");
                if (isDirectory)
                {
                    if (!seen.Add(id)) continue;
                    if (seen.Count > 20_000) throw new IOException("Subsonic 目录超过 20,000 个，请缩小同步范围。");
                    pending.Enqueue(id);
                    continue;
                }
                var song = SubsonicSongFacts.From(child, directory);
                if (song is not null) tracks.Add(song.ToTrack(source.Id));
            }
            if (tracks.Count > 0) yield return new(tracks, false);
        }
    }
    private IEnumerable<SubsonicSongFacts> Songs(JsonElement album) => Many(album, "song").Select(song => SubsonicSongFacts.From(song, album)).OfType<SubsonicSongFacts>();
    private static AlbumSummary? Summary(JsonElement album)
    {
        var id = Id(album);
        if (id.Length == 0) return null;
        return new(id, RemoteBrowseRules.Text(album, "name", 512), RemoteBrowseRules.Text(album, "artist", 512), RemoteBrowseRules.Text(album, "coverArt", 256),
            RemoteBrowseRules.Number(album, "songCount"), RemoteBrowseRules.Number(album, "duration"), RemoteBrowseRules.Text(album, "created", 64),
            Year(album), RemoteBrowseRules.Text(album, "genre", 128));
    }
    private static string Fingerprint(AlbumSummary album) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f',
        album.Id, album.Name, album.Artist, album.Cover, album.SongCount, album.Duration, album.Created, album.Year, album.Genre))));
    private static (string Kind, string Id) ScanRoot(RemoteConnection source)
    {
        var root = string.IsNullOrWhiteSpace(source.RootPath) ? "/" : source.RootPath;
        if (root == "/") return ("", "");
        try
        {
            var location = RemoteBrowseRules.VirtualId(root);
            return location.Kind is "f" or "d" or "a" or "b" ? (location.Kind, location.Id) : ("", "");
        }
        catch (ArgumentException) { return ("", ""); }
    }
    private static int? Year(JsonElement item)
    {
        var year = RemoteBrowseRules.Number(item, "year");
        return year is >= 1 and <= 9999 ? (int)year : null;
    }
    private static SubsonicSongFacts[]? ReadCachedSongs(string payload)
    {
        try
        {
            using var json = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 8 });
            if (json.RootElement.ValueKind != JsonValueKind.Array) return null;
            return json.RootElement.EnumerateArray().Select(item => SubsonicSongFacts.From(item, default)).OfType<SubsonicSongFacts>().ToArray();
        }
        catch (JsonException) { return null; }
    }
    private static string? WriteSongs(IReadOnlyList<SubsonicSongFacts> songs)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var song in songs) song.Write(writer);
            writer.WriteEndArray();
        }
        return buffer.Length is 0 or > 262144 ? null : Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
    private sealed record AlbumSummary(string Id, string Name, string Artist, string Cover, long? SongCount, long? Duration, string Created, int? Year, string Genre);
}
