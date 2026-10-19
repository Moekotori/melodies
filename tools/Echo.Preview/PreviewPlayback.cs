using System.Text.Json;
using Echo.Application.Playback;
using Echo.Playback;

// Explicit render-only fixture. Production composition never creates an embedded player.
internal sealed class PreviewPlayback : IDisposable
{
    public PlaybackRuntime Runtime { get; } = new(new UnavailableAudio());
    public void Dispose() => Runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
    private sealed class UnavailableAudio : INativeAudio
    {
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default)
            => Task.FromException(new InvalidOperationException("此渲染预览没有音频输出。"));
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
            => Task.FromException<JsonElement>(new InvalidOperationException("此渲染预览没有音频输出。"));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
