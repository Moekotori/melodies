using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Echo.Wallpapers;

public sealed class WallpaperRuntime
{
    private WallpaperRenderState _state = new();
    public int? ProcessId { get; private set; }
    public WallpaperRenderState State { get => Volatile.Read(ref _state); set => Volatile.Write(ref _state, value); }
    public async Task RenderAsync(string manifest, string browser, string? assets, string cacheRoot,
        Func<byte[], CancellationToken, Task> display, Func<WallpaperMediaState>? media, CancellationToken token)
    {
        await using var server = new WallpaperAssetServer(manifest, assets);
        await using var process = new WallpaperBrowserProcess(browser, cacheRoot);
        ProcessId = process.ProcessId;
        try
        {
            await using var tools = new WallpaperDevTools();
            await tools.ConnectAsync(await process.PageEndpoint(token).ConfigureAwait(false), token).ConfigureAwait(false);
            await tools.Command("Page.enable", "{}", token);
            await tools.Command("Runtime.enable", "{}", token);
            await tools.Command("Page.setDownloadBehavior", "{\"behavior\":\"deny\"}", token);
            await Resize(tools, State, token);
            await tools.Command("Page.navigate", "{\"url\":" + WallpaperPages.Quote(server.Url) + "}", token);
            var start = Stopwatch.GetTimestamp(); var lastMedia = start; var ready = false; var audio = false; var pointer = false;
            WallpaperRenderState? applied = null;
            var frozen = false;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var state = State;
                var frameStart = Stopwatch.GetTimestamp();
                if (applied?.Width != state.Width || applied.Height != state.Height) await Resize(tools, state, token);
                if (state.Paused && ready)
                {
                    if (!frozen)
                    {
                        await Send(tools, "{\"type\":\"echo-we:paused\",\"paused\":true}", token);
                        await tools.Command("Page.setWebLifecycleState", "{\"state\":\"frozen\"}", token); frozen = true;
                    }
                    applied = state;
                    await Task.Delay(100, token).ConfigureAwait(false); continue;
                }
                if (frozen)
                {
                    await tools.Command("Page.setWebLifecycleState", "{\"state\":\"active\"}", token);
                    await Send(tools, "{\"type\":\"echo-we:paused\",\"paused\":false}", token); frozen = false;
                }
                if (!ready || Stopwatch.GetElapsedTime(lastMedia).TotalSeconds >= 1)
                {
                    using var status = await tools.Evaluate("window.__echoStatus || null", token);
                    var result = status.RootElement.GetProperty("result").GetProperty("result");
                    if (result.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object)
                    {
                        ready = value.GetProperty("ready").GetBoolean(); audio = value.GetProperty("audio").GetBoolean(); pointer = value.GetProperty("pointer").GetBoolean();
                        if (value.GetProperty("failed").GetBoolean()) throw new InvalidDataException("壁纸场景加载失败：" + (tools.LastError ?? "WebGL 或场景资源不可用"));
                    }
                    if (!ready && Stopwatch.GetElapsedTime(start).TotalSeconds > 20) throw new InvalidDataException("动态壁纸加载超时：" + (tools.LastError ?? "未收到画面就绪确认"));
                }
                if (applied?.FramesPerSecond != state.FramesPerSecond || !ready)
                    await Send(tools, $"{{\"type\":\"echo-we:frame-interval\",\"frameIntervalMs\":{1000 / Math.Clamp(state.FramesPerSecond, 1, 30)}}}", token);
                applied = state;
                if (ready)
                {
                    if (pointer) await Send(tools, FormPointer(state), token);
                    var timeline = Stopwatch.GetElapsedTime(lastMedia).TotalSeconds >= 1;
                    if (media is not null && (audio || timeline))
                    { await PushMedia(tools, media(), audio, timeline, token); }
                    if (timeline) lastMedia = Stopwatch.GetTimestamp();
                    using var capture = await tools.Call("Page.captureScreenshot", "{\"format\":\"jpeg\",\"quality\":88,\"fromSurface\":true,\"captureBeyondViewport\":false,\"optimizeForSpeed\":true}", token);
                    var encoded = capture.RootElement.GetProperty("result").GetProperty("data").GetString()!;
                    if (encoded.Length > 6 * 1024 * 1024) throw new InvalidDataException("壁纸画面数据过大");
                    await display(Convert.FromBase64String(encoded), token).ConfigureAwait(false);
                }
                var delay = 1000d / Math.Clamp(state.FramesPerSecond, 1, 30) - Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds;
                if (delay > 0) await Task.Delay(TimeSpan.FromMilliseconds(delay), token).ConfigureAwait(false);
            }
        }
        finally { ProcessId = null; }
    }
    private static Task Resize(WallpaperDevTools tools, WallpaperRenderState state, CancellationToken token)
    {
        var width = Math.Clamp(state.Width, 16, 1280); var height = Math.Clamp(state.Height, 16, 1280);
        return tools.Command("Emulation.setDeviceMetricsOverride", $"{{\"width\":{width},\"height\":{height},\"deviceScaleFactor\":1,\"mobile\":false}}", token);
    }
    private static Task Send(WallpaperDevTools tools, string json, CancellationToken token)
        => tools.Command("Runtime.evaluate", "{\"expression\":" + WallpaperPages.Quote("window.__echoSend && window.__echoSend(" + json + ")") + "}", token);
    private static string FormPointer(WallpaperRenderState state)
        => Form(writer => { writer.WriteString("type", "echo-we:pointer"); writer.WriteNumber("x", Math.Clamp(state.PointerX, 0, 1)); writer.WriteNumber("y", Math.Clamp(state.PointerY, 0, 1)); });
    private static string Form(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream(); using (var writer = new Utf8JsonWriter(buffer)) { writer.WriteStartObject(); write(writer); writer.WriteEndObject(); }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
    private static async Task PushMedia(WallpaperDevTools tools, WallpaperMediaState state, bool audio, bool timeline, CancellationToken token)
    {
        var messages = new List<string>(4);
        if (timeline)
        {
        messages.Add(Form(w =>
        {
            w.WriteString("type", "echo-we:media"); w.WriteStartObject("properties"); w.WriteString("title", state.Title); w.WriteString("artist", state.Artist);
            w.WriteString("albumTitle", state.Album); w.WriteString("contentType", "music"); w.WriteEndObject();
        }));
        messages.Add(Form(w => { w.WriteString("type", "echo-we:playback"); w.WriteNumber("state", state.Playback); }));
        messages.Add(Form(w => { w.WriteString("type", "echo-we:timeline"); w.WriteNumber("position", state.Position); w.WriteNumber("duration", state.Duration); }));
        }
        if (audio) messages.Add(Form(w =>
        {
            w.WriteString("type", "echo-we:audio"); w.WriteStartArray("samples");
            for (var channel = 0; channel < 2; channel++)
            for (var bin = 0; bin < 64; bin++)
            {
                var spectrum = state.Spectrum; var sample = 0d;
                if (state.Playback == 1 && spectrum is { Count: > 1 })
                {
                    var position = bin * (spectrum.Count - 1) / 63d; var low = (int)position; var high = Math.Min(spectrum.Count - 1, low + 1);
                    sample = spectrum[low] * (1 - (position - low)) + spectrum[high] * (position - low);
                }
                w.WriteNumberValue(double.IsFinite(sample) ? Math.Clamp(sample, 0, 1) : 0);
            }
            w.WriteEndArray();
        }));
        await tools.Command("Runtime.evaluate", "{\"expression\":" + WallpaperPages.Quote("[" + string.Join(',', messages) + "].forEach(window.__echoSend)") + "}", token);
    }
}
