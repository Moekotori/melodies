using System.Text.Json;
using Echo.Playback;

// Simulates native receipts for presentation/regression checks. No real audio device.
internal sealed class PreviewAudio : INativeAudio, INativeOutputControl
{
    public bool RejectOutput { get; set; }
    public int Configurations { get; private set; }
    public event Action<string, JsonElement>? Notification { add { } remove { } }
    public event Action<Exception>? Failed { add { } remove { } }
    public int OutputChannels => 2;
    public bool ProDspAllowed => true;
    public JsonElement? Capabilities => Json("{\"wasapiExclusive\":true,\"asio\":true}");
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetOutputChannelsAsync(int channels, CancellationToken token = default, bool forceRestart = false) => Task.CompletedTask;
    public Task<IReadOnlyList<AudioOutputDevice>> EnumerateOutputDevicesAsync(string mode, CancellationToken token = default)
        => Task.FromResult<IReadOnlyList<AudioOutputDevice>>([new("studio", "Studio DAC · USB Audio", mode, 0, true, 48000, [])]);
    public Task<JsonElement> ProbeAsioDsdAsync(AudioOutputDevice device, CancellationToken token = default) => Task.FromResult(Json("{}"));
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        if (method == "device.configure")
        {
            Configurations++;
            if (RejectOutput) throw new IOException("Preview device rejected output");
            return Task.FromResult(Json("{\"accepted\":true,\"sampleRate\":48000,\"channels\":2}"));
        }
        if (method == "queue.set") return Task.FromResult(Json($"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}"));
        if (method == "audio.sessionBegin") return Task.FromResult(Json("{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}"));
        return Task.FromResult(Json("true"));
    }
    private static JsonElement Json(string text) { using var document = JsonDocument.Parse(text); return document.RootElement.Clone(); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
