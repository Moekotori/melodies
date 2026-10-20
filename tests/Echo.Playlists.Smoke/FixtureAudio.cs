using System.Text.Json;
using Echo.Playback;

internal sealed class FixtureAudio : INativeAudio
{
    private int operation;
    public event Action<string, JsonElement>? Notification { add { } remove { } }
    public event Action<Exception>? Failed { add { } remove { } }
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        var json = method switch
        {
            "queue.set" => "{\"queueRevision\":" + parameters!.Value.GetProperty("revision").GetInt64() + "}",
            "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2}}",
            "audio.openFile" or "audio.seek" => "{\"operationId\":" + ++operation + ",\"sampleRate\":48000,\"durationSeconds\":200}",
            _ => "true"
        };
        using var result = JsonDocument.Parse(json); return Task.FromResult(result.RootElement.Clone());
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
