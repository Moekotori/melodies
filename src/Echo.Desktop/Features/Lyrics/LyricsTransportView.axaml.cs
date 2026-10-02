using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
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
    private double availableWidth = 900;
    private bool compact;
    public bool IsCompact => compact;
    public bool IsInteracting => IsPointerOver || IsKeyboardFocusWithin || VolumeButton.Flyout?.IsOpen == true || Model?.Player.IsSeeking == true;
    public event Action? InteractionChanged;

    public LyricsTransportView()
    {
        InitializeComponent();
        seek = new(SeekSlider, () => Model?.Player, true, () => ElapsedText.SetCurrentValue(TextBlock.TextProperty, Model?.Player.PositionText ?? "0:00"));
        SeekSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && Model?.Player.IsSeeking == true)
                ElapsedText.SetCurrentValue(TextBlock.TextProperty, TimeSpan.FromSeconds(SeekSlider.Value).ToString(SeekSlider.Value >= 3600 ? @"h\:mm\:ss" : @"m\:ss"));
        };
        var volume = new Flyout { Placement = PlacementMode.Top, Content = new PlayerVolumePanel() };
        popover.Wrap(volume); VolumeButton.Flyout = volume;
        volume.Opened += (_, _) => InteractionChanged?.Invoke();
        volume.Closed += (_, _) => InteractionChanged?.Invoke();
        PointerEntered += (_, _) => { SetCompact(false); InteractionChanged?.Invoke(); };
        PointerExited += (_, _) => InteractionChanged?.Invoke();
        GotFocus += (_, _) => { SetCompact(false); InteractionChanged?.Invoke(); };
        LostFocus += (_, _) => InteractionChanged?.Invoke();
        DataContextChanged += (_, _) => seek.Cancel();
    }
    public void Bind(ShellViewModel value) { shell = value; popover.Attach(value); ApplyMotion(); }
    public void ApplyMotion()
    {
        var duration = shell?.Motion.CanAnimate == true ? TimeSpan.FromMilliseconds(160) : TimeSpan.Zero;
        BarSurface.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = duration }];
        IdleSurface.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = duration }];
    }
    public void ApplyPalette(bool dark)
    {
        Resources["LyricsInk"] = new SolidColorBrush(Color.Parse(dark ? "#F4F5F8" : "#272B36"));
        Resources["LyricsSurface"] = new SolidColorBrush(Color.Parse(dark ? "#EE242630" : "#F2FAFAFC"));
        Resources["LyricsEdge"] = new SolidColorBrush(Color.Parse(dark ? "#28FFFFFF" : "#20272B36"));
        Resources["LyricsRail"] = new SolidColorBrush(Color.Parse(dark ? "#38FFFFFF" : "#30272B36"));
        Resources["LyricsHover"] = new SolidColorBrush(Color.Parse(dark ? "#20FFFFFF" : "#14272B36"));
    }
    public void Fit(double width)
    {
        availableWidth = width;
        var mini = Model?.Appearance.MiniPlayer != false;
        var barWidth = Math.Max(280, Math.Min(width - 48, mini ? 880 : 1440));
        TransportLayout.Width = barWidth;
        TransportLayout.Height = mini ? 96 : 116;
        IdleSurface.Width = Math.Min(340, barWidth);
        var narrow = barWidth < 680;
        var tiny = barWidth < 500;
        TrackInfo.IsVisible = !tiny;
        ArtistText.IsVisible = !narrow;
        CoverFrame.Width = CoverFrame.Height = mini ? 44 : 60;
        TrackInfo.ColumnDefinitions[0].Width = new GridLength(mini ? 44 : 60);
        ControlRow.ColumnDefinitions[0].Width = tiny ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        ControlRow.ColumnDefinitions[2].Width = narrow ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        ControlRow.ColumnSpacing = narrow ? 8 : 18;
        ModeButton.IsVisible = FavoriteButton.IsVisible = !narrow;
        BarSurface.Padding = new Thickness(narrow ? 14 : 20, mini ? 10 : 12);
    }
    public void SetCompact(bool value)
    {
        value &= Model?.Appearance is { MiniPlayer: true, PlayerCompact: true } && !IsInteracting;
        if (value) seek.Cancel();
        compact = value;
        BarSurface.IsHitTestVisible = BarSurface.IsEnabled = !value;
        BarSurface.Opacity = value ? 0 : 1;
        IdleSurface.IsHitTestVisible = value; IdleSurface.Opacity = value ? 1 : 0;
    }
    private void OnQueue(object? sender, RoutedEventArgs e) => shell?.ToggleQueueDrawerCommand.Execute(null);
    private void OnBarMode(object? sender, RoutedEventArgs e)
    {
        if (Model is not { } model) return;
        model.Appearance.MiniPlayer = !model.Appearance.MiniPlayer;
        SetCompact(false); Fit(availableWidth);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { seek.Cancel(); VolumeButton.Flyout?.Hide(); popover.Detach(); base.OnDetachedFromVisualTree(e); }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); popover.Attach(shell); }
}
