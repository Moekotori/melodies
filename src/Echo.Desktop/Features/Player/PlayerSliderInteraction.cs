using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Echo.Desktop.Features.Player;

// A drag owns only its local preview. Commit once on release; reject a changed track/control.
internal sealed class PlayerSliderInteraction
{
    private readonly Slider slider;
    private readonly Func<PlayerViewModel?> resolvePlayer;
    private readonly bool seek;
    private PlayerViewModel? active;
    private string path = "";
    public PlayerSliderInteraction(Slider slider, Func<PlayerViewModel?> player, bool seek)
    {
        this.slider = slider; resolvePlayer = player; this.seek = seek;
        slider.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        { if (e.GetCurrentPoint(slider).Properties.IsLeftButtonPressed) Begin(); }, RoutingStrategies.Tunnel, true);
        slider.AddHandler(InputElement.PointerReleasedEvent, async (_, _) => await CommitAsync(), RoutingStrategies.Bubble, true);
        slider.PointerCaptureLost += (_, _) => Dispatcher.UIThread.Post(Cancel, DispatcherPriority.Background);
        slider.AddHandler(InputElement.KeyDownEvent, (_, e) => { if (IsAdjustment(e.Key)) Begin(); }, RoutingStrategies.Tunnel, true);
        slider.KeyUp += async (_, e) => { if (IsAdjustment(e.Key)) await CommitAsync(); };
        slider.LostFocus += (_, _) => Cancel();
    }
    private static bool IsAdjustment(Key key) => key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown;
    private void Begin()
    {
        if (active is not null || !slider.IsEnabled || resolvePlayer() is not { } player) return;
        active = player; path = player.CurrentFilePath;
        if (seek) player.IsSeeking = true; else player.IsChangingVolume = true;
    }
    private async Task CommitAsync()
    {
        if (active is not { } player) return;
        var value = slider.Value;
        active = null;
        try
        {
            if (resolvePlayer() == player && (!seek || player.CurrentFilePath == path))
            { if (seek) await player.SeekToAsync(value); else await player.ChangeVolumeAsync(value); }
        }
        finally
        {
            if (seek) { player.IsSeeking = false; slider.Value = player.Position; }
            else { player.IsChangingVolume = false; slider.Value = player.Volume; }
        }
    }
    public void Cancel()
    {
        if (active is not { } player) return;
        active = null;
        if (seek) { player.IsSeeking = false; slider.Value = player.Position; }
        else { player.IsChangingVolume = false; slider.Value = player.Volume; }
    }
}
