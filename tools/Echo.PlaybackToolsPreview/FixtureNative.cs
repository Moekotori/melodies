using System.Text.Json;
using Echo.Playback;

internal sealed class FixtureNative : INativeAudio
{
    private int operation;
    public JsonElement LastQueue { get; private set; }
    public int SeekCalls { get; private set; }
    public int PauseCalls { get; private set; }
    public bool RejectQueue { get; set; }
    public bool RejectPause { get; set; }
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public void Position(long id, double seconds) => Emit("audio.position", FormattableString.Invariant($"{{\"operationId\":{id},\"framesPlayed\":{seconds * 48000}}}"));
    public void Started(long id) => Emit("audio.started", $"{{\"operationId\":{id}}}");
    private void Emit(string method, string value)
    {
        using var json = JsonDocument.Parse(value);
        Notification?.Invoke(method, json.RootElement.Clone());
    }
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        if (method == "queue.set")
        {
            if (RejectQueue) { RejectQueue = false; throw new IOException("fixture queue rejection"); }
            LastQueue = parameters!.Value.Clone();
        }
        if (method == "audio.seek") SeekCalls++;
        if (method == "audio.pause") { PauseCalls++; if (RejectPause) throw new IOException("fixture pause failure"); }
        var result = method switch
        {
            "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"format\":\"float32le\"}}",
            "audio.openFile" or "audio.openSource" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":120}}",
            "audio.seek" => $"{{\"operationId\":{++operation}}}",
            _ => "true"
        };
        using var document = JsonDocument.Parse(result);
        return Task.FromResult(document.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
