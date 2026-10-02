using System.Text.Json;
using Echo.Playback;

sealed class TestHost : INativeAudio
{
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed { add { } remove { } }
    private int operation;
    public void Emit(string method, string json)
    { using var document = JsonDocument.Parse(json); Notification?.Invoke(method, document.RootElement.Clone()); }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        var json = method switch
        {
            "queue.set" => $"{{\"queueRevision\":{parameters!.Value.GetProperty("revision").GetInt64()}}}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000}}",
            "audio.openFile" => $"{{\"operationId\":{++operation},\"sampleRate\":48000,\"durationSeconds\":40}}",
            "audio.seek" => $"{{\"operationId\":{++operation}}}", _ => "true"
        };
        using var document = JsonDocument.Parse(json); return Task.FromResult(document.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
