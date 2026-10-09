using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
        DataContextChanged += (_, _) => { volumeInteraction.Cancel(); speedInteraction.Cancel(); ObservePlayer(); ApplyVolumeLock(); ShowPane("volume"); };
        AddHandler(KeyDownEvent, OnPanelKey, RoutingStrategies.Tunnel);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        volumeInteraction.Cancel(); speedInteraction.Cancel();
        if (observedPlayer is not null) observedPlayer.PropertyChanged -= PlayerChanged;
        observedPlayer = null;
        ShowPane("volume");
        base.OnDetachedFromVisualTree(e);
    }
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
    private async void SelectPane(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string pane }) return;
        ShowPane(pane);
        if (pane == "output" && Player is { } player)
            await player.AudioSettings.RefreshCommand.ExecuteAsync(null);
    }
    private void ShowPane(string pane)
    {
        volumeInteraction.Cancel(); speedInteraction.Cancel();
        foreach (var tab in PaneTabs.Children.OfType<Button>())
            tab.Classes.Set("active", Equals(tab.Tag, pane));
        VolumePane.IsVisible = pane == "volume";
        ExtraPane.IsVisible = pane != "volume";
        // Create only the selected feature view; removing it releases its bindings.
        ExtraPane.Content = pane switch
        {
            "output" => new PlayerQuickOutputPanel(),
            "sleep" => new PlayerSleepTimerPanel { DataContext = Player?.Tools },
            "mini" => new PlayerMiniPanel(),
            _ => null
        };
    }
    private void OnPanelKey(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None ||
            (DataContext as ShellViewModel)?.Shortcuts.IsRecording == true) return;
        if (e.Key == Key.Escape) { CloseRequested?.Invoke(); e.Handled = true; return; }
        var tabs = PaneTabs.Children.OfType<Button>().ToArray();
        var index = Array.FindIndex(tabs, tab => tab.IsFocused);
        if (index >= 0 && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            index = e.Key switch { Key.Home => 0, Key.End => tabs.Length - 1,
                Key.Left => (index + tabs.Length - 1) % tabs.Length, _ => (index + 1) % tabs.Length };
            tabs[index].Focus(); SelectPane(tabs[index], new RoutedEventArgs()); e.Handled = true;
        }
        else if (VolumePane.IsVisible && e.Key is Key.Q or Key.E or Key.M)
        {
            if (e.Key == Key.Q) SpeedSlider.Focus();
            else if (e.Key == Key.E) VolumeSlider.Focus();
            else ToggleMute(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
