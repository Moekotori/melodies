using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Onboarding;

public partial class OperationGuideView : UserControl
{
    private readonly MainWindow? window;
    private readonly ShellViewModel? shell;
    private readonly Action? close;
    private Control? target;
    private int index;
    private int targetAttempts;
    private OperationGuideStep Step => OperationGuideSteps.All[index];
    public int StepIndex => index;
    public OperationGuideView() => InitializeComponent();
    public OperationGuideView(MainWindow window, ShellViewModel shell, Action close) : this()
    {
        this.window = window; this.shell = shell; this.close = close;
        AttachedToVisualTree += (_, _) => { window.LayoutUpdated += OnLayout; ShowStep(); };
        DetachedFromVisualTree += (_, _) => { window.LayoutUpdated -= OnLayout; target = null; };
    }
    private void ShowStep()
    {
        if (shell is null) return;
        target = null;
        targetAttempts = 4;
        var libraryButton = window!.GetVisualDescendants().OfType<Button>().FirstOrDefault(control => control.Name == "LibraryButton");
        libraryButton?.Flyout?.Hide();
        shell.CloseQueueDrawerCommand.Execute(null);
        shell.CloseQuickSettingsCommand.Execute(null);
        shell.CloseGlobalSearchCommand.Execute(null);
        if (Step.Route is { } route) shell.NavigateCommand.Execute(route);
        if (Step.Id is "lyrics-style" or "lyrics-background") OpenVisuals();
        StepTitle.Text = shell.Text[$"onboarding.guide.{Step.Id}.title"];
        StepBody.Text = shell.Text[$"onboarding.guide.{Step.Id}.body"];
        Counter.Text = $"{index + 1:00} / {OperationGuideSteps.All.Count:00}";
        BackButton.IsEnabled = index > 0;
        ContinueButton.Content = shell.Text[index == OperationGuideSteps.All.Count - 1 ? "onboarding.guide.done" : "onboarding.next"];
        ActionButton.IsVisible = Step.Action is not null;
        if (Step.Action is { } action) ActionButton.Content = shell.Text["onboarding.guide.action." + action];
        StepTitle.Focus();
        Place();
    }
    private void OnLayout(object? sender, EventArgs e) => Place();
    private void Place()
    {
        if (window is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        // Re-query only when the named target is missing or replaced by navigation/Ultralight.
        if (target is not null && (!target.IsAttachedToVisualTree() || !target.IsEffectivelyVisible)) { target = null; targetAttempts = 4; }
        if (target is null && targetAttempts > 0)
        {
            targetAttempts--;
            target = Step.Target is { } name ? window.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name && control.IsEffectivelyVisible) : null;
        }
        Rect? area = null;
        if (target is { Bounds.Width: > 0, Bounds.Height: > 0 } && target.TranslatePoint(default, this) is { } origin
            && target.TranslatePoint(new Point(target.Bounds.Width, target.Bounds.Height), this) is { } end)
        {
            var clipped = new Rect(origin, end).Intersect(new Rect(Bounds.Size)).Inflate(5);
            if (clipped.Width > 0 && clipped.Height > 0) area = clipped;
        }
        Highlight.IsVisible = area is not null;
        if (area is { } rect)
        {
            Canvas.SetLeft(Highlight, Math.Max(4, rect.Left)); Canvas.SetTop(Highlight, Math.Max(42, rect.Top));
            Highlight.Width = Math.Min(rect.Width, Bounds.Width - 8); Highlight.Height = Math.Min(rect.Height, Bounds.Height - 48);
        }
        Card.Width = Math.Min(400, Math.Max(260, Bounds.Width - 32));
        Card.MaxHeight = Math.Max(180, Bounds.Height - 80);
        var height = Math.Min(Card.DesiredSize.Height > 0 ? Card.DesiredSize.Height : 340, Card.MaxHeight);
        var x = (Bounds.Width - Card.Width) / 2;
        var y = (Bounds.Height - height) / 2;
        if (area is { } anchor)
        {
            if (anchor.Left > Card.Width + 40) x = anchor.Left - Card.Width - 20;
            else if (Bounds.Width - anchor.Right > Card.Width + 40) x = anchor.Right + 20;
            else y = anchor.Top > height + 60 ? anchor.Top - height - 20 : anchor.Bottom + 20;
        }
        Canvas.SetLeft(Card, Math.Clamp(x, 16, Math.Max(16, Bounds.Width - Card.Width - 16)));
        Canvas.SetTop(Card, Math.Clamp(y, 48, Math.Max(48, Bounds.Height - height - 16)));
    }
    private void OnClose(object? sender, RoutedEventArgs e) => close?.Invoke();
    private void OnBack(object? sender, RoutedEventArgs e) { if (index > 0) { index--; ShowStep(); } }
    private void OnNext(object? sender, RoutedEventArgs e)
    { if (index == OperationGuideSteps.All.Count - 1) close?.Invoke(); else { index++; ShowStep(); } }
    private void OpenVisuals()
    {
        if (shell is null) return;
        if (shell.QuickSettingsKind != "lyrics") shell.OpenQuickSettingsCommand.Execute("lyrics");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!this.IsAttachedToVisualTree()) return;
            var tabs = window!.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(control => control.Name == "LyricsTabs");
            if (tabs is not null) tabs.SelectedIndex = 1;
            target = null;
            targetAttempts = 4;
        });
    }
    private void OnAction(object? sender, RoutedEventArgs e)
    {
        if (shell is null) return;
        switch (Step.Action)
        {
            case "import":
                var button = window!.GetVisualDescendants().OfType<Button>().FirstOrDefault(control => control.Name == "LibraryButton" && control.IsEffectivelyVisible);
                button?.Flyout?.ShowAt(button); break;
            case "queue": if (!shell.IsQueueDrawerOpen) shell.ToggleQueueDrawerCommand.Execute(null); break;
            case "lyrics": shell.OpenNowPlayingCommand.Execute(null); break;
            case "visuals": case "background": OpenVisuals(); break;
            case "settings": shell.NavigateCommand.Execute("settings"); shell.Settings.SelectCategoryCommand.Execute("general"); break;
        }
    }
}
