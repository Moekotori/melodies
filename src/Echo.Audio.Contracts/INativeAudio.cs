using System.Text.Json;

namespace Echo.Playback;

public interface INativeAudio : IAsyncDisposable
{
    event Action<string, JsonElement>? Notification;
    event Action<Exception>? Failed;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default);
}
