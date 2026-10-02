using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsTransportView : UserControl
{
    private ShellViewModel? shell;
    private LyricsViewModel? Model => DataContext as LyricsViewModel;
    private readonly PlayerSliderInteraction seek;
    private readonly PlayerPopoverBinding popover = new();
    private readonly Echo.Desktop.Motion.MotionFlyout signalFlyout;
    public event Action? InteractionChanged;

    public LyricsTransportView()
    {
        InitializeComponent();
        ExpandedLayout.Clip = contentClip;
        CapsuleSurface.PropertyChanged += (_, e) =>
        { if (e.Property == Controls.Lyrics.LyricsPlayerSurface.ExpansionProperty || e.Property == BoundsProperty) UpdateContentClip(); };
        ExpandedLayout.SizeChanged += (_, _) => UpdateContentClip();
        signalFlyout = SignalPathPanel.Create(MoreButton, () => Model?.Player,
            () => shell?.OpenQuickSettingsCommand.Execute("audio"));
        popover.Wrap(signalFlyout);
        signalFlyout.Opened += (_, _) => InteractionChanged?.Invoke();
        signalFlyout.Closed += (_, _) => InteractionChanged?.Invoke();
        seek = new(SeekSlider, () => Model?.Player, true, () =>
        { ElapsedText.SetCurrentValue(TextBlock.TextProperty, Model?.Player.PositionText ?? "0:00"); InteractionChanged?.Invoke(); });
        SeekSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && Model?.Player.IsSeeking == true)
                ElapsedText.SetCurrentValue(TextBlock.TextProperty, TimeSpan.FromSeconds(SeekSlider.Value).ToString(SeekSlider.Value >= 3600 ? @"h\:mm\:ss" : @"m\:ss"));
        };
        var volume = new Flyout { Placement = PlacementMode.Top, Content = new PlayerVolumePanel() };
        VolumeButton.Flyout = volume;
        foreach (var flyout in new[] { volume, (Flyout)MoreButton.Flyout! })
        {
            popover.Wrap(flyout);
            flyout.Opened += (_, _) => InteractionChanged?.Invoke();
            flyout.Closed += (_, _) => InteractionChanged?.Invoke();
        }
        PointerEntered += (_, _) => InteractionChanged?.Invoke();
        PointerExited += (_, _) => InteractionChanged?.Invoke();
        GotFocus += (_, e) => { keyboardInteraction = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional; InteractionChanged?.Invoke(); };
        LostFocus += (_, _) => InteractionChanged?.Invoke();
        KeyDown += (_, _) => { keyboardInteraction = true; InteractionChanged?.Invoke(); };
        DataContextChanged += (_, _) => seek.Cancel();
    }
    public void Bind(ShellViewModel value) { shell = value; popover.Attach(value); ApplyMotion(); }
    public void ApplyPalette()
    {
        Resources["LyricsInk"] = new SolidColorBrush(Color.Parse("#FAFAFA"));
        Resources["LyricsControlInk"] = new SolidColorBrush(Color.Parse("#C4C3C2"));
        Resources["LyricsSurface"] = new SolidColorBrush(Color.Parse("#232120"));
        Resources["LyricsRail"] = new SolidColorBrush(Color.Parse("#585655"));
        Resources["LyricsProgress"] = new SolidColorBrush(Color.Parse("#7770DF"));
        Resources["LyricsHover"] = new SolidColorBrush(Color.Parse("#16FFFFFF"));
    }
    public void Fit(double width)
    {
        var mini = Model?.Appearance.MiniPlayer != false;
        var barWidth = Math.Max(280, Math.Min(width - (width >= 1050 ? 108 : 48), mini ? 760 : 1440));
        TransportLayout.Width = barWidth;
        var narrow = barWidth < 1050;
        TransportLayout.Height = BarSurface.Height = mini ? 56 : narrow ? 64 : 88;
        IdleSurface.Width = Math.Min(340, barWidth);
        var tiny = barWidth < 500;
        ModeButton.IsVisible = QueueButton.IsVisible = LyricsButton.IsVisible = barWidth >= 640;
        FavoriteButton.IsVisible = !tiny;
        foreach (var button in PlaybackControls.Children.OfType<Button>().Concat(AuxiliaryControls.Children.OfType<Button>()))
            button.Width = button.Height = narrow ? tiny ? 32 : 36 : 44;
        PlaybackControls.Spacing = tiny ? 6 : mini ? 10 : narrow ? 12 : 28;
        AuxiliaryControls.Spacing = narrow ? 8 : 12;
        BarSurface.Padding = new Thickness(tiny ? 12 : mini ? 16 : narrow ? 20 : 28, 0);
        ProgressRow.Margin = new Thickness(tiny ? 8 : mini ? 12 : narrow ? 16 : 32, 0, tiny ? 8 : mini ? 18 : narrow ? 20 : 60, 0);
        ProgressRow.ColumnSpacing = tiny ? 6 : narrow ? 8 : 16;
        ProgressRow.ColumnDefinitions[0].Width = new GridLength(tiny ? 32 : narrow ? 40 : 52);
        ProgressRow.ColumnDefinitions[2].Width = new GridLength(tiny ? 0 : narrow ? 40 : 52);
        DurationLabel.IsVisible = !tiny;
        ElapsedText.FontSize = DurationLabel.FontSize = narrow ? 13 : 18;
    }
    private void OnQueue(object? sender, RoutedEventArgs e)
    { MoreButton.Flyout?.Hide(); shell?.ToggleQueueDrawerCommand.Execute(null); }
    private void OnLyricsSettings(object? sender, RoutedEventArgs e)
    {
        MoreButton.Flyout?.Hide(); shell?.OpenQuickSettingsCommand.Execute("lyrics");
    }
    private void OnAudioSettings(object? sender, RoutedEventArgs e)
    { MoreButton.Flyout?.Hide(); shell?.OpenQuickSettingsCommand.Execute("audio"); }
    private void OnSignalPath(object? sender, RoutedEventArgs e)
    { MoreButton.Flyout?.Hide(); signalFlyout.ShowAt(MoreButton); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { seek.Cancel(); StopMotion(); signalFlyout.Hide(); VolumeButton.Flyout?.Hide(); MoreButton.Flyout?.Hide(); popover.Detach(); base.OnDetachedFromVisualTree(e); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); popover.Attach(shell); }
}
