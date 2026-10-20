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
    private static readonly string[] Tones = ["good", "proc", "warn", "danger", "muted"];

    // The root carries exactly one tone class; styles key the glow and headline color off it.
    private void ApplyTone(string tone)
    {
        foreach (var name in Tones) Root.Classes.Set(name, name == tone);
    }

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
            panel.Width = Math.Clamp((top?.ClientSize.Width ?? 900) - 48, 280, 424);
            panel.MaxHeight = Math.Clamp((top?.ClientSize.Height ?? 800) - 140, 240, 720);
            panel.DiagnosticsToggle.IsChecked = false;
            panel.DoctorToggle.IsChecked = false;
            var view = player() is { } model ? new SignalPathViewModel(model) : null;
            panel.DataContext = view;
            panel.ApplyTone(view?.Tone ?? "muted");
            if (view is not null) view.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SignalPathViewModel.Tone)) panel.ApplyTone(view.Tone); };
            panel.ContentScroll.Offset = default;
            panel.CloseButton.Focus();
        };
        flyout.Closed += (_, _) => { (panel.DataContext as SignalPathViewModel)?.Dispose(); panel.DataContext = null; };
        return flyout;
    }
}
