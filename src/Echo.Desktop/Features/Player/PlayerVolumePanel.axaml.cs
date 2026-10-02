using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Player;

public partial class PlayerVolumePanel : UserControl
{
    private readonly PlayerSliderInteraction volumeInteraction;
    private readonly PlayerSliderInteraction speedInteraction;
    private PlayerViewModel? Player => (DataContext as ShellViewModel)?.Player;
    private double restoreVolume = 1;
    private bool volumeLocked => Player?.VolumeLocked == true;
    private PlayerViewModel? observedPlayer;
    public event Action? CloseRequested;

    public PlayerVolumePanel()
    {
        InitializeComponent();
        volumeInteraction = new(VolumeSlider, () => Player, seek: false);
        speedInteraction = new(SpeedSlider, () => Player, seek: false, playbackRate: true);
        VolumeSlider.PropertyChanged += (_, e) =>
        { if (e.Property == Slider.ValueProperty) VolumeReadout.Text = $"{VolumeSlider.Value * 100:0}%"; };
        SpeedSlider.PropertyChanged += (_, e) =>
        { if (e.Property == Slider.ValueProperty) SpeedReadout.Text = SpeedSlider.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "x"; };
        DataContextChanged += (_, _) => { volumeInteraction.Cancel(); speedInteraction.Cancel(); ObservePlayer(); ApplyVolumeLock(); };
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { volumeInteraction.Cancel(); speedInteraction.Cancel(); if (observedPlayer is not null) observedPlayer.PropertyChanged -= PlayerChanged; observedPlayer = null; base.OnDetachedFromVisualTree(e); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); ObservePlayer(); ApplyVolumeLock(); }
    private void ObservePlayer()
    {
        if (observedPlayer is not null) observedPlayer.PropertyChanged -= PlayerChanged;
        observedPlayer = Player;
        if (observedPlayer is not null) observedPlayer.PropertyChanged += PlayerChanged;
    }
    private void PlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(PlayerViewModel.VolumeLocked)) { volumeInteraction.Cancel(); ApplyVolumeLock(); } }

    private async void ToggleMute(object? sender, RoutedEventArgs e)
    {
        if (volumeLocked || Player is not { IsBusy: false } player) return;
        if (player.Volume > 0) { restoreVolume = player.Volume; await player.ChangeVolumeAsync(0); }
        else await player.ChangeVolumeAsync(restoreVolume);
    }
    private async void ToggleVolumeLock(object? sender, RoutedEventArgs e)
    { volumeInteraction.Cancel(); if (Player is { } player) await player.ToggleVolumeLockAsync(); ApplyVolumeLock(); }
    private void ApplyVolumeLock()
    { VolumeAdjustment.IsEnabled = !volumeLocked; MuteButton.IsEnabled = !volumeLocked; VolumeLockButton.Classes.Set("active", volumeLocked); }
    private async void DecreaseSpeed(object? sender, RoutedEventArgs e) => await StepSpeedAsync(-.05);
    private async void IncreaseSpeed(object? sender, RoutedEventArgs e) => await StepSpeedAsync(.05);
    private async void ResetSpeed(object? sender, RoutedEventArgs e)
    { if (Player is { IsBusy: false } player) await player.ChangePlaybackRateAsync(1); }
    private async Task StepSpeedAsync(double step)
    { if (Player is { IsBusy: false } player) await player.ChangePlaybackRateAsync(Math.Clamp(Math.Round(player.PlaybackRate + step, 2), .5, 2)); }
    private async void DecreaseVolume(object? sender, RoutedEventArgs e) => await StepVolumeAsync(-.05);
    private async void IncreaseVolume(object? sender, RoutedEventArgs e) => await StepVolumeAsync(.05);
    private async Task StepVolumeAsync(double step)
    { if (!volumeLocked && Player is { IsBusy: false } player) await player.ChangeVolumeAsync(Math.Clamp(Math.Round(player.Volume + step, 2), 0, 1)); }
    private void OpenOutput(object? sender, RoutedEventArgs e)
    { var shell = DataContext as ShellViewModel; CloseRequested?.Invoke(); shell?.OpenQuickSettingsCommand.Execute("audio"); }
    private void ToggleMini(object? sender, RoutedEventArgs e)
    { var shell = DataContext as ShellViewModel; CloseRequested?.Invoke(); shell?.Floating.ToggleMiniCommand.Execute(null); }
}
