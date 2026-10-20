using System.Text;
using Echo.Application.Playback;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Core;
using Echo.Core.Client;
using Echo.Core.Contracts;
using Echo.Core.Hosting;
using Echo.Core.Transport;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Playlists;

internal static class PlaylistCoreChecks
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        var databasePath = Path.Combine(root, "library.sqlite"); var media = Path.Combine(root, "music");
        Directory.CreateDirectory(media);
        await using (var seed = await LibraryDatabase.OpenAsync(databasePath, token))
        {
            var job = await seed.BeginScanAsync(media, false, token);
            for (var offset = 0; offset < 1001; offset += 200)
            {
                var batch = Enumerable.Range(offset, Math.Min(200, 1001 - offset)).Select(index =>
                {
                    var path = Path.Combine(media, $"歌曲-{index:0000}.flac");
                    if (index < 245) File.WriteAllBytes(path, [0]);
                    return new ScanEntry(new(path, LibraryPath.Key(path), 123, 1), ScanEntryKind.Metadata,
                        new($"Song {index:0000}", "Artist, \"A\"", "Album", "Artist", 180, "FLAC", 48000, 24));
                }).ToArray();
                await seed.CommitScanBatchAsync(job, offset / 200 + 1, batch, token);
            }
            await seed.FinishScanAsync(job, ScanStatus.Completed, 1001, false, null, token);
        }
        var fileName = Path.Combine(root, "collection.m3u8");
        var paths = Enumerable.Range(0, 245).Select(index => Path.Combine(media, $"歌曲-{index:0000}.flac")).ToArray();
        var input = Encoding.UTF8.GetBytes("#EXTM3U\n#PLAYLIST:Collection\n" + string.Join('\n', paths) + "\n" + paths[0] + "\nmissing.flac\nhttps://example.invalid/private\n");
        string deletedId;
        await using (var runtime = new CoreRuntime(new PlaybackRuntime(new FixtureAudio()), databasePath))
        using (var serverStop = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            var endpoint = new CoreEndpoint(root, databasePath);
            var server = CoreServiceHost.RunAsync(runtime, endpoint, TimeSpan.FromMinutes(1), serverStop.Token);
            try
            {
                await using var first = await CoreConnection.ConnectAsync(endpoint, token: token);
                await using var second = await CoreConnection.ConnectAsync(endpoint, token: token);
                var op = Guid.NewGuid().ToString("N");
                var imported = await first.Library.ImportPlaylistFileAsync(op, fileName, input, token);
                var repeated = await second.Library.ImportPlaylistFileAsync(op, fileName, input, token);
                Check(imported == repeated && imported.Imported == 246 && imported.Skipped == 2, "atomic import receipt and skipped counts");
                var tracks = await second.Library.GetPlaylistTracksAsync(new(imported.PlaylistId), token);
                Check(tracks.Total == 246 && tracks.Items.Count == 200, "import crosses a UI page");
                var tail = await second.Library.GetPlaylistTracksAsync(new(imported.PlaylistId, 200), token);
                Check(tail.Items[^1].Track.Id == tracks.Items[0].Track.Id && tail.Items[^1].ItemId != tracks.Items[0].ItemId, "duplicate occurrences preserved");
                foreach (var format in new[] { "m3u8", "m3u", "pls", "json", "csv", "xspf", "wpl", "txt" })
                {
                    var exported = await first.Library.ExportPlaylistFileAsync(new(imported.PlaylistId), "Collection", format, token);
                    var parsed = PlaylistFileReader.Read(Path.Combine(root, "roundtrip." + format), exported.Content);
                    Check(exported.Tracks == 246 && parsed.Paths.SequenceEqual(paths.Append(paths[0])), format + " full ordered export/reimport");
                }
                var filtered = await first.Library.ExportPlaylistFileAsync(new(imported.PlaylistId, Search: "Song 0000"), "Filtered", "json", token);
                Check(filtered.Tracks == 2, "export honors SQL search");
                await first.Library.PlayPlaylistAsync(new(imported.PlaylistId), cancellation: token);
                Check(runtime.Playback.LocalZone.Session.Queue.Count == 246, "Core playback includes page two");
                while (second.Playback.LocalZone.Session.Queue.Count != 246) await Task.Delay(5, token);
                var original = runtime.Playback.LocalZone.Session.Queue.Select(item => item.TrackId).ToArray();
                await first.Library.PlayPlaylistAsync(new(imported.PlaylistId), shuffle: true, cancellation: token);
                Check(runtime.Playback.LocalZone.Session.Queue.Count == 246 && runtime.Playback.LocalZone.Session.Queue.Select(item => item.TrackId).Order().SequenceEqual(original.Order()), "shuffle retains all occurrences");
                var before = (await first.Library.GetPlaylistsAsync(new(), token)).Total;
                await Reject(() => first.Library.ImportPlaylistFileAsync(Guid.NewGuid().ToString("N"), fileName, Encoding.UTF8.GetBytes("unindexed.flac"), token));
                Check((await second.Library.GetPlaylistsAsync(new(), token)).Total == before, "zero-match import rolls back");
                await first.Library.ArchivePlaylistAsync(Guid.NewGuid().ToString("N"), imported.PlaylistId, true, token);
                Check((await second.Library.GetPlaylistsAsync(new(Archived: true), token)).Items.Single().Id == imported.PlaylistId, "archive visible across controls");
                deletedId = imported.PlaylistId;
                await using var bridge = await CoreConnection.ConnectAsync(endpoint, "bridge", token);
                try { await bridge.Library.ImportPlaylistFileAsync(Guid.NewGuid().ToString("N"), fileName, input, token); throw new Exception("Bridge gained write access"); }
                catch (CoreRemoteException error) when (error.Code == "forbidden") { }
                Console.WriteLine("PASS real IPC: two controls, atomic receipt, skipped paths, duplicate occurrences, 8 formats, cross-page playback, archive and Bridge denial");
            }
            finally { serverStop.Cancel(); try { await server; } catch (OperationCanceledException) { } }
        }
        await using (var reopened = await LibraryDatabase.OpenAsync(databasePath, token))
        {
            Check((await reopened.GetPlaylistsAsync(new(Archived: true), token)).Items.Single().Id == deletedId, "archive survives owner restart");
            await reopened.ArchivePlaylistAsync(Guid.NewGuid().ToString("N"), deletedId, false, token);
            Check((await reopened.GetPlaylistTracksAsync(new(deletedId), token)).Total == 246, "restored content preserved");
        }
        await using (var core = new CoreRuntime(new PlaybackRuntime(new FixtureAudio()), databasePath))
        {
            var large = await core.Library.CreatePlaylistAsync(Guid.NewGuid().ToString("N"), "Large", "", token);
            for (var offset = 0; offset < 1001; offset += 200)
            {
                var page = await core.Library.GetTracksAsync(new(200, Offset: offset), token);
                await core.Library.AddPlaylistTracksAsync(Guid.NewGuid().ToString("N"), large, page.Items.Select(track => track.Id).ToArray(), token);
            }
            await Reject(() => core.Library.PlayPlaylistAsync(new(large), cancellation: token));
            Check(core.Playback.LocalZone.Session.Queue.Count == 0, "oversized whole-play request leaves queue intact");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            await Reject(() => core.Library.ImportPlaylistFileAsync(Guid.NewGuid().ToString("N"), fileName, input, canceled.Token));
        }
        ParserChecks(root);
        Console.WriteLine("PASS restart recovery, cancelled import, explicit queue capacity and parser security boundaries");
    }

    private static void ParserChecks(string root)
    {
        var file = Path.Combine(root, "relative.m3u8");
        var parsed = PlaylistFileReader.Read(file, Encoding.UTF8.GetBytes("music/歌曲-0000.flac\nhttps://example.invalid/a\n"));
        Check(parsed.Paths.Single() == Path.Combine(root, "music", "歌曲-0000.flac") && parsed.Skipped == 1, "relative paths and non-file URI");
        try { PlaylistFileReader.Read(Path.Combine(root, "bad.xspf"), Encoding.UTF8.GetBytes("<!DOCTYPE a [<!ENTITY x SYSTEM 'file:///secret'>]><a>&x;</a>")); throw new Exception("DTD accepted"); }
        catch (System.Xml.XmlException) { }
        try { PlaylistFileReader.Read(file, new byte[PlaylistFileReader.MaximumImportBytes + 1]); throw new Exception("oversize accepted"); }
        catch (ArgumentException) { }
        try { PlaylistFileReader.Read(Path.Combine(root, "bad.csv"), Encoding.UTF8.GetBytes("path\n\"unclosed")); throw new Exception("broken CSV accepted"); }
        catch (ArgumentException) { }
        var remoteBase = PlaylistFileReader.Read(Path.Combine(root, "base.xspf"), Encoding.UTF8.GetBytes("<playlist xmlns='http://xspf.org/ns/0/' xml:base='https://example.invalid/'><trackList><track><location>/music/song.flac</location></track></trackList></playlist>"));
        Check(remoteBase.Paths.Count == 0 && remoteBase.Skipped == 1, "remote XML base cannot become a local path");
    }
    private static async Task Reject(Func<Task> action)
    { try { await action(); } catch (Exception error) when (error is CoreRemoteException or InvalidOperationException or ArgumentException or OperationCanceledException) { return; } throw new Exception("Expected request rejection"); }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
