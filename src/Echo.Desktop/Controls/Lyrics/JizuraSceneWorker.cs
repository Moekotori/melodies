using System.Text.Json;
using System.Threading.Channels;
using Echo.Contracts.Lyrics;
using Echo.Visuals;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed record JizuraRequest(long Version, LyricsDocument? Document, int Window,
    string Title, string Artist, string Look, double Seconds, bool Motion, bool Words);
internal sealed record JizuraReceipt(long Version, RasterImageFrame? Frame, string? Error, string? Diagnostic = null);

// One worker and one replaceable request. Native graphics and interpretation never run on the UI.
internal sealed class JizuraSceneWorker : IDisposable
{
    private readonly Channel<JizuraRequest> requests = Channel.CreateBounded<JizuraRequest>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource stop = new();
    private readonly Action<JizuraReceipt> deliver;
    private readonly Task running;
    public JizuraSceneWorker(Action<JizuraReceipt> deliver)
    { this.deliver = deliver; running = Task.Run(Run); }
    public void Request(JizuraRequest value) => requests.Writer.TryWrite(value);
    private async Task Run()
    {
        RasterFramePool? frames = null;
        JizuraEngine? engine = null; long version = -1; int window = -1; bool failed = false;
        try
        {
            await foreach (var request in requests.Reader.ReadAllAsync(stop.Token))
            {
                if (request.Document is null)
                {
                    engine?.Dispose(); engine = null; frames?.Dispose(); frames = null;
                    version = -1; window = -1; failed = false; continue;
                }
                if (failed && version == request.Version) continue;
                try
                {
                    if (version != request.Version || window != request.Window)
                    {
                        engine?.Dispose(); engine = null; version = request.Version; window = request.Window; failed = false;
                        engine = new JizuraEngine(stop.Token);
                        var project = JizuraProjectBuilder.Create(request.Document, window, request.Title, request.Artist, request.Look, request.Words);
                        engine.Build(JsonSerializer.Serialize(project, JizuraProjectJson.Default.JizuraProject));
                    }
                    var image = engine!.DrawFrame(request.Document.IsSynced ? request.Seconds : .5, request.Motion, frames ??= new RasterFramePool());
                    if (image is null) continue; // All slots still belong to presentation; keep the latest request only.
                    if (stop.IsCancellationRequested) { image.Dispose(); break; }
                    deliver(new(request.Version, image, null));
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    failed = true; engine?.Dispose(); engine = null;
                    deliver(new(request.Version, null, "Afterglow 场景暂时无法绘制，请切换主题或重新进入歌词界面。", ex.ToString()));
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally { engine?.Dispose(); frames?.Dispose(); }
    }
    public void Dispose()
    {
        stop.Cancel(); requests.Writer.TryComplete();
        _ = running.ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
