using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Desktop.Shell;
using Echo.Playback;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Player;

public partial class PlayerBar : UserControl
{
    private PlayerViewModel? Player => (DataContext as ShellViewModel)?.Player;
    private readonly PlayerSliderInteraction seekInteraction;
    private readonly MotionFlyout outputFlyout;
    private readonly PlayerPopoverBinding popoverBinding = new();
    public PlayerBar()
    {
        InitializeComponent();
        seekInteraction = new(SeekSlider, () => Player, seek: true,
            () => ElapsedText.SetCurrentValue(TextBlock.TextProperty, Player?.PositionText ?? "0:00"));
        outputFlyout = new() { Placement = PlacementMode.Top, Content = new PlayerOutputPanel() };
        foreach (var flyout in new[] { SignalPathButton.Flyout, OutputSettingsButton.Flyout, VolumeButton.Flyout, outputFlyout }.OfType<Flyout>()) popoverBinding.Wrap(flyout);
        SizeChanged += (_, _) => ApplyLayout();
        SeekSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && Player?.IsSeeking == true)
                ElapsedText.SetCurrentValue(TextBlock.TextProperty, TimeSpan.FromSeconds(SeekSlider.Value).ToString(SeekSlider.Value >= 3600 ? @"h\:mm\:ss" : @"m\:ss"));
        };
        DataContextChanged += (_, _) => { seekInteraction.Cancel(); outputFlyout.Hide(); popoverBinding.Attach(DataContext as ShellViewModel); };
    }
    private void ApplyLayout()
    {
        var width = Bounds.Width;
        var small = width <= 1180;
        var compact = width <= 720;
        var tiny = width <= 600;
        PlayerLayout.ColumnDefinitions[1].Width = new GridLength(small ? tiny ? 240 : compact ? 288 : 360 : Math.Clamp(width * 0.48, 560, 760));
        PlayerLayout.ColumnDefinitions[2].Width = small ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        PlayerLayout.ColumnSpacing = compact ? 8 : 16;
        BarSurface.Padding = new Thickness(small ? 16 : 28, 6);
        var cover = tiny ? 48 : small ? 64 : 84;
        NowPlaying.ColumnDefinitions[0].Width = new GridLength(cover);
        NowPlaying.ColumnSpacing = small ? 10 : 14;
        CoverButton.Width = CoverButton.Height = CoverArtwork.Width = CoverArtwork.Height = cover;
        SignalPathButton.IsVisible = !compact;
        FavoriteButton.IsVisible = !tiny;
        QueueButton.IsVisible = !tiny;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { seekInteraction.Cancel(); HidePopovers(); outputFlyout.Hide(); popoverBinding.Detach(); base.OnDetachedFromVisualTree(e); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); popoverBinding.Attach(DataContext as ShellViewModel); }
    private void HidePopovers()
    { SignalPathButton.Flyout?.Hide(); OutputSettingsButton.Flyout?.Hide(); VolumeButton.Flyout?.Hide(); }
    private async void OpenOutputSettings(object? sender, RoutedEventArgs args)
    {
        HidePopovers();
        outputFlyout.ShowAt(OutputSettingsButton);
        if (Player is { } player) await player.AudioSettings.RefreshCommand.ExecuteAsync(null);
    }
    private void OpenLyrics(object? sender, RoutedEventArgs args)
    { HidePopovers(); (DataContext as ShellViewModel)?.NavigateCommand.Execute("lyrics"); }
    private void OpenQueue(object? sender, RoutedEventArgs args)
    { HidePopovers(); (DataContext as ShellViewModel)?.ToggleQueueDrawerCommand.Execute(null); }
    private async void OpenFiles(object? sender, RoutedEventArgs args)
    {
        HidePopovers();
        if (Player is not { } player || TopLevel.GetTopLevel(this) is not { } top || player.IsBusy) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "打开音频文件", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("音频") { Patterns = ["*.mp3", "*.flac", "*.wav", "*.m4a", "*.aac", "*.ogg", "*.opus", "*.aiff", "*.ape", "*.wv", "*.dsf", "*.dff", "*.wma", "*.ac3", "*.mka"] }, FilePickerFileTypes.All]
            });
            var paths = files.Select(x => x.TryGetLocalPath()).OfType<string>().ToArray();
            await player.OpenPathsAsync(paths);
        }
        catch (Exception) { player.Error = "无法打开文件，请重新选择。"; }
    }
}
