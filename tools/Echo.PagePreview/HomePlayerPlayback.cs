using System.Text.Json;
using Echo.Application.Playback;
using Echo.Playback;

// Explicit protocol fixture: renders confirmed states without opening an audio device.
internal sealed class HomePlayerPlayback : INativeAudio, IDisposable
{
    private long operation;
    public PlaybackRuntime Runtime { get; }
    public int Pauses { get; private set; }
    public int Seeks { get; private set; }
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed { add { } remove { } }
    public HomePlayerPlayback() => Runtime = new(this);
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Started() => Emit("audio.started", $"{{\"operationId\":{operation}}}");
    public void Position(double seconds) => Emit("audio.position", $"{{\"operationId\":{operation},\"framesPlayed\":{(long)(seconds * 48000)}}}");
    private void Emit(string method, string json)
    { using var document = JsonDocument.Parse(json); Notification?.Invoke(method, document.RootElement.Clone()); }
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        if (method == "audio.pause") Pauses++;
        if (method == "audio.seek") Seeks++;
        var json = method switch
        {
            "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
            "audio.openFile" or "audio.seek" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":223}}",
            "device.enumerate" => "[]",
            _ => "true"
        };
        using var document = JsonDocument.Parse(json); return Task.FromResult(document.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() => Runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
}
