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
        Resources["LyricsControlInk"] = new SolidColorBrush(Color.Parse("#D3D2DB"));
        Resources["LyricsSurface"] = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(Color.Parse("#303038"), 0), new GradientStop(Color.Parse("#202026"), 1)] };
        Resources["LyricsRail"] = new SolidColorBrush(Color.Parse("#50FFFFFF"));
        Resources["LyricsProgress"] = new SolidColorBrush(Color.Parse("#AAA4F6"));
        Resources["LyricsHover"] = new SolidColorBrush(Color.Parse("#16FFFFFF"));
    }
    public void Fit(double width)
    {
        var mini = Model?.Appearance.MiniPlayer != false;
        var barWidth = Math.Max(280, Math.Min(width - (width >= 1050 ? 108 : 48), mini ? 740 : 1120));
        TransportLayout.Width = barWidth;
        TransportLayout.Height = BarSurface.Height = mini ? 52 : 60;
        IdleSurface.Width = Math.Min(320, barWidth);
        var tiny = barWidth < 500;
        ModeButton.IsVisible = QueueButton.IsVisible = LyricsButton.IsVisible = barWidth >= 640;
        FavoriteButton.IsVisible = !tiny;
        foreach (var button in PlaybackControls.Children.OfType<Button>().Concat(AuxiliaryControls.Children.OfType<Button>()))
            button.Width = button.Height = tiny ? 30 : 32;
        PlaybackControls.Spacing = tiny ? 4 : mini ? 6 : 10;
        AuxiliaryControls.Spacing = 8;
        BarSurface.Padding = new Thickness(tiny ? 10 : 16, 0);
        ProgressRow.Margin = new Thickness(tiny ? 8 : 16, 0, tiny ? 8 : 20, 0);
        ProgressRow.ColumnSpacing = tiny ? 6 : 10;
        ProgressRow.ColumnDefinitions[0].Width = new GridLength(tiny ? 32 : 42);
        ProgressRow.ColumnDefinitions[2].Width = new GridLength(tiny ? 0 : 42);
        DurationLabel.IsVisible = !tiny;
        ElapsedText.FontSize = DurationLabel.FontSize = 12;
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
