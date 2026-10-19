using Echo.Application.Playback;
using Echo.Core.Library;

namespace Echo.Core;

/// <summary>Embedded Core composition. Controls borrow its services; only the application disposes it.</summary>
public sealed class CoreRuntime : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly ListeningRecorder listening;
    private readonly bool ownsPlayback;
    private Task? shutdown;

    public PlaybackRuntime Playback { get; }
    public LibraryCore Library { get; }
    public event Action? ListeningChanged;
    public event Action<string>? ListeningDiagnostic;

    public CoreRuntime(PlaybackRuntime playback, string libraryPath, bool ownsPlayback = true)
    {
        Playback = playback;
        this.ownsPlayback = ownsPlayback;
        Library = new(libraryPath, playback.BackgroundWork);
        listening = new(playback.LocalZone, Library,
            () => Notify(ListeningChanged), message => Notify(ListeningDiagnostic, message));
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(shutdown ??= Task.Run(ShutdownAsync));
    }

    private async Task ShutdownAsync()
    {
        try { await listening.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            try { await Library.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                try { if (ownsPlayback) await Playback.DisposeAsync().ConfigureAwait(false); }
                finally { ListeningChanged = null; ListeningDiagnostic = null; }
            }
        }
    }

    private static void Notify(Action? handlers)
    {
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
            try { handler(); } catch (Exception) { /* A control cannot interrupt Core persistence. */ }
    }

    private static void Notify(Action<string>? handlers, string message)
    {
        if (handlers is null) return;
        foreach (Action<string> handler in handlers.GetInvocationList())
            try { handler(message); } catch (Exception) { /* A disconnected control owns its error. */ }
    }
}
