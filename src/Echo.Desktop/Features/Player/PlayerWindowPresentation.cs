using Avalonia;
using Avalonia.Controls;

namespace Echo.Desktop.Features.Player;

// Each window owns its display clock. Playback facts and commands remain connected while hidden.
internal sealed class PlayerWindowPresentation : IDisposable
{
    private readonly Window window;
    private readonly PlayerViewModel player;
    private bool disposed;

    public PlayerWindowPresentation(Window window, PlayerViewModel player)
    {
        this.window = window; this.player = player;
        window.PropertyChanged += Changed;
        window.Closed += Closed;
        Refresh();
    }

    private void Changed(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty) Refresh();
    }

    private void Refresh() => player.SetClockPresentationActive(window.IsVisible && window.WindowState != WindowState.Minimized);
    private void Closed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.PropertyChanged -= Changed;
        window.Closed -= Closed;
        player.SetClockPresentationActive(false);
    }
}
