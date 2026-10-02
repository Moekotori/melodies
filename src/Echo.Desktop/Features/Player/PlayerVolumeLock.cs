using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel
{
    [ObservableProperty] private bool volumeLocked;
    private void ObserveVolumeLock(bool value) => Dispatcher.UIThread.Post(() =>
    { if (Volatile.Read(ref detached) == 0) VolumeLocked = value; });
    public Task ToggleVolumeLockAsync() => RunAsync(() => session.SetVolumeLockedAsync(!session.VolumeLocked));
}
