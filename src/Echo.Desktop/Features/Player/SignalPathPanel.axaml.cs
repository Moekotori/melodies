using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Player;

public partial class SignalPathPanel : UserControl
{
    public event Action? CloseRequested;
    public event Action? OutputSettingsRequested;
    public SignalPathPanel() => InitializeComponent();
    private void Close(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
    private void OpenSettings(object? sender, RoutedEventArgs e) => OutputSettingsRequested?.Invoke();

    public static MotionFlyout Create(Control target, Func<PlayerViewModel?> player, Action settings)
    {
        var panel = new SignalPathPanel();
        var flyout = new MotionFlyout { Placement = PlacementMode.Top, Content = panel,
            FlyoutPresenterClasses = { "signal-path-presenter" } };
        panel.CloseRequested += () => flyout.Hide();
        panel.OutputSettingsRequested += () => { flyout.Hide(); settings(); };
        flyout.Opened += (_, _) =>
        {
            (panel.DataContext as SignalPathViewModel)?.Dispose();
            var top = TopLevel.GetTopLevel(target);
            panel.Width = Math.Clamp((top?.ClientSize.Width ?? 900) - 48, 280, 400);
            panel.MaxHeight = Math.Clamp((top?.ClientSize.Height ?? 800) - 140, 240, 540);
            panel.DiagnosticsExpander.IsExpanded = false;
            panel.DoctorExpander.IsExpanded = false;
            panel.DataContext = player() is { } model ? new SignalPathViewModel(model) : null;
            panel.ContentScroll.Offset = default;
            panel.CloseButton.Focus();
        };
        flyout.Closed += (_, _) => { (panel.DataContext as SignalPathViewModel)?.Dispose(); panel.DataContext = null; };
        return flyout;
    }
}
