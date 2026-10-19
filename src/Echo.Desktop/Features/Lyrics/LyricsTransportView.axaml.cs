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
    public void ApplyPalette(bool lightInk = true)
    {
        CapsuleSurface.Dark = lightInk;
        Resources["LyricsInk"] = new SolidColorBrush(Color.Parse(lightInk ? "#FAFAFA" : "#292D37"));
        Resources["LyricsControlInk"] = new SolidColorBrush(Color.Parse(lightInk ? "#D3D2DB" : "#4A4F5C"));
        Resources["LyricsSurface"] = new SolidColorBrush(Color.Parse(lightInk ? "#B82B303B" : "#C4F4F7FC"));
        Resources["LyricsRail"] = new SolidColorBrush(Color.Parse(lightInk ? "#30FFFFFF" : "#28292D37"));
        Resources["LyricsFocusRail"] = new SolidColorBrush(Color.Parse(lightInk ? "#66FFFFFF" : "#50292D37"));
        Resources["LyricsProgress"] = new SolidColorBrush(Color.Parse(lightInk ? "#F0F0F5" : "#4A4F5C"));
        Resources["LyricsHover"] = new SolidColorBrush(Color.Parse(lightInk ? "#16FFFFFF" : "#12292D37"));
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
