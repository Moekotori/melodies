using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Echo.Visuals;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed record SourceSceneRequest(long Revision, string Mode, SourceSceneProject? Project, int Width, int Height,
    double PositionMs, bool Motion, bool Playing, double PointerX = -1, double PointerY = -1, bool Press = false);
internal sealed record SourceSceneReceipt(long Revision, SourceSceneFrame? Frame, string? Error, string? Diagnostic = null, double? SeekMs = null);

// One serialized owner and one replaceable pending request. A slow scene cannot queue frames or block the UI/audio.
internal sealed class SourceSceneWorker : IDisposable
{
    private readonly Channel<SourceSceneRequest> requests = Channel.CreateBounded<SourceSceneRequest>(new BoundedChannelOptions(1)
    { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource stop = new();
    private readonly Action<SourceSceneReceipt> deliver;
    private readonly Task running;
    private SourceSceneRequest? pendingPress;
    public SourceSceneWorker(Action<SourceSceneReceipt> deliver) { this.deliver = deliver; running = Task.Run(Run); }
    public void Request(SourceSceneRequest request)
    {
        if (request.Press) Interlocked.Exchange(ref pendingPress, request);
        requests.Writer.TryWrite(request);
    }
    private async Task Run()
    {
        SourceLyricsEngine? engine = null; long revision = -1; bool failed = false;
        var clock = Stopwatch.StartNew(); double previous = 0;
        try
        {
            await foreach (var request in requests.Reader.ReadAllAsync(stop.Token))
            {
                if (request.Project is null) { engine?.Dispose(); engine = null; revision = -1; continue; }
                if (failed && revision == request.Revision) continue;
                try
                {
                    if (revision != request.Revision)
                    {
                        engine?.Dispose(); engine = null; revision = request.Revision; failed = false;
                        engine = new(request.Mode, cancellation: stop.Token);
                        engine.Build(JsonSerializer.Serialize(request.Project, SourceSceneJson.Default.SourceSceneProject), request.Width, request.Height);
                        if (request.Mode == "cassette" && request.Project.Artwork is { } artwork) engine.SetCover(artwork);
                        previous = clock.Elapsed.TotalMilliseconds;
                    }
                    var press = Interlocked.Exchange(ref pendingPress, null);
                    var pointer = press?.Revision == request.Revision ? press : request;
                    var seek = pointer.PointerX >= 0 ? engine!.Pointer(pointer.PointerX, pointer.PointerY, pointer.Press) : -1;
                    var now = clock.Elapsed.TotalMilliseconds;
                    var picture = engine!.Draw(request.PositionMs, request.Motion, now, Math.Clamp(now - previous, 1, 1000), request.Playing);
                    previous = now;
                    if (stop.IsCancellationRequested) { picture.Dispose(); break; }
                    deliver(new(request.Revision, new(picture, Math.Max(1, (int)picture.CullRect.Width), Math.Max(1, (int)picture.CullRect.Height)), null, SeekMs: seek >= 0 ? seek : null));
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    failed = true; engine?.Dispose(); engine = null;
                    deliver(new(request.Revision, null, "场景绘制失败", error.ToString()));
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { engine?.Dispose(); }
    }
    public void Dispose()
    {
        stop.Cancel(); requests.Writer.TryComplete();
        _ = running.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
