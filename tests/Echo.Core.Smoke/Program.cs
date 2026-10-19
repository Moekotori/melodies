using System.Text.Json;
using Echo.Application.Playback;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Core;
using Echo.Core.Library;
using Echo.Playback;

using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
var token = budget.Token;
var root = Path.GetFullPath(Path.Combine("misc", "core-smoke", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var path = Path.Combine(root, "library.sqlite");
var audio = Path.Combine(root, "song.wav");
using (var writer = new BinaryWriter(File.Create(audio)))
{
    const int size = 4800 * 4;
    writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16);
    writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000);
    writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(size); writer.Write(new byte[size]);
}
var native = new IdleAudio();
await using var core = new CoreRuntime(new PlaybackRuntime(native), path);
// Canceling one control's lazy-open wait must leave the shared owner usable by another control.
using var canceled = new CancellationTokenSource(); canceled.Cancel();
try { await core.Library.GetTracksAsync(new(), canceled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { }
ITrackLibrary control = core.Library;
Check((await control.GetTracksAsync(new(), token)).Items.Count == 0, "Shared opening was canceled");
await using (var rival = new LibraryCore(path))
{
    try { await rival.GetTracksAsync(new(), token); throw new Exception("Duplicate writer admitted"); }
    catch (IOException) { }
}
// Import through Core's actual scan path, then browse and mutate through existing capability contracts.
var scan = await core.Library.ImportAsync(audio, new Progress<ScanProgress>(), token);
var page = await control.GetTracksAsync(new(), token);
Check(scan.Status == ScanStatus.Completed && scan.Inserted == 1 && page.Items.Count == 1, "Core import lost its track");
var repeat = await core.Library.ImportAsync(audio, new Progress<ScanProgress>(), token);
Check(repeat.Unchanged == 1 && repeat.Inserted == 0, "Incremental import changed");
IAlbumPersonalState secondControl = core.Library;
await secondControl.SetTrackFavoriteAsync(page.Items[0].Id, true, token);
Check((await control.GetTracksAsync(new(), token)).Items.Single().Favorite, "Controls did not share committed state");
// With no window or UI event loop, a watched root remains Core-owned and is released on shutdown.
await core.Library.SetFolderWatchAsync(root, true, token);
await Task.WhenAll(core.DisposeAsync().AsTask(), core.DisposeAsync().AsTask());
Check(native.Disposals == 1, "Shared shutdown disposed playback more than once");
try { await control.GetTracksAsync(new(), token); throw new Exception("Disposed Core accepted a request"); }
catch (ObjectDisposedException) { }
await using (var reopened = new LibraryCore(path))
    Check((await reopened.GetTracksAsync(new(), token)).Items.Count == 1, "Shutdown leaked ownership or lost committed data");
// Shutdown while the lazy database is opening must release the writer lock as well.
var earlyPath = Path.Combine(root, "early.sqlite");
var early = new LibraryCore(earlyPath);
var opening = early.GetTracksAsync(new(), token);
await Task.WhenAll(early.DisposeAsync().AsTask(), early.DisposeAsync().AsTask());
try { await opening; } catch (OperationCanceledException) { } catch (ObjectDisposedException) { }
await using (var reopened = new LibraryCore(earlyPath))
    _ = await reopened.GetTracksAsync(new(), token);
var borrowedAudio = new IdleAudio();
await using var borrowedPlayback = new PlaybackRuntime(borrowedAudio);
await using (var borrowed = new CoreRuntime(borrowedPlayback, Path.Combine(root, "borrowed.sqlite"), ownsPlayback: false)) { }
Check(borrowedAudio.Disposals == 0, "Core disposed externally owned playback");
Console.WriteLine("PASS shared lazy database, per-control cancellation, single writer, real incremental scan, shared mutations, watcher shutdown, opening/shutdown race, borrowed playback");

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
sealed class IdleAudio : INativeAudio
{
    public int Disposals { get; private set; }
    public event Action<string, JsonElement>? Notification { add { } remove { } }
    public event Action<Exception>? Failed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("No device should start");
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("No device RPC expected");
    public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
}
