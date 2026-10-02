using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Player;

public partial class PlayerVolumePanel : UserControl
{
    private readonly PlayerSliderInteraction interaction;
    private PlayerViewModel? Player => (DataContext as ShellViewModel)?.Player;
    private double restoreVolume = 1;
    public PlayerVolumePanel()
    {
        InitializeComponent();
        interaction = new(VolumeSlider, () => Player, seek: false);
        VolumeSlider.PropertyChanged += (_, e) =>
        { if (e.Property == Slider.ValueProperty) VolumeReadout.Text = $"{VolumeSlider.Value * 100:0}%"; };
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { interaction.Cancel(); base.OnDetachedFromVisualTree(e); }
    private async void ToggleMute(object? sender, RoutedEventArgs e)
    {
        if (Player is not { } player) return;
        if (player.Volume > 0) { restoreVolume = player.Volume; await player.ChangeVolumeAsync(0); }
        else await player.ChangeVolumeAsync(restoreVolume);
    }
    private async void ResetVolume(object? sender, RoutedEventArgs e)
    { if (Player is { } player) await player.ChangeVolumeAsync(1); }
}
