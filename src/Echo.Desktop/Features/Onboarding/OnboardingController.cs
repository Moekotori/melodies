using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Onboarding;

// Owned by the Control window. No timers, database access or playback lifecycle ownership.
internal sealed class OnboardingController : IDisposable
{
    private readonly MainWindow window;
    private readonly ShellViewModel shell;
    private FirstRunViewModel? setup;
    private Control? view;
    private IInputElement? returnFocus;
    private bool disposed;
    public const string CompletedKey = "onboarding-completed";
    public OnboardingController(MainWindow window, ShellViewModel shell)
    {
        this.window = window; this.shell = shell;
        shell.OnboardingRequested += Open;
        window.Closed += OnClosed;
        window.AddHandler(InputElement.KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
    }
    public void StartIfNeeded()
    {
        if (!disposed && view is null && !window.IsExitRequested && !shell.FeaturePreferences.Read(CompletedKey, new()).Enabled) Open(true);
    }
    private void Open(bool wizard)
    {
        if (disposed || window.IsExitRequested || shell.Player is null) return;
        Close(); window.RestoreMainWindow();
        returnFocus = window.FocusManager?.GetFocusedElement();
        shell.CloseQueueDrawerCommand.Execute(null); shell.CloseQuickSettingsCommand.Execute(null);
        shell.CloseGlobalSearchCommand.Execute(null); shell.CloseSidebarDrawerCommand.Execute(null);
        shell.Shortcuts.CancelRecording();
        shell.IsSetupOpen = wizard;
        if (wizard)
        {
            setup = new(shell, Complete);
            var firstRun = new FirstRunView { DataContext = setup };
            view = firstRun; window.SetOnboardingView(view);
            Dispatcher.UIThread.Post(() => { if (ReferenceEquals(view, firstRun)) firstRun.FocusStart(); });
        }
        else { view = new OperationGuideView(window, shell, Close); window.SetOnboardingView(view); }
    }
    private void Complete(bool tour)
    {
        shell.FeaturePreferences.Confirm(CompletedKey, new(true));
        Close();
        if (tour) Open(false);
    }
    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (view is null || e.Key != Key.Escape) return;
        e.Handled = true;
        if (setup?.IsBusy == true) return;
        if (setup is not null) Complete(false); else Close();
    }
    public void Close()
    {
        if (view is null) return;
        shell.IsSetupOpen = false;
        window.SetOnboardingView(null); view = null;
        setup?.Dispose(); setup = null;
        if (returnFocus is Control { IsEffectivelyVisible: true } control) control.Focus();
        returnFocus = null;
    }
    public void Dispose()
    {
        disposed = true; Close(); shell.OnboardingRequested -= Open;
        window.Closed -= OnClosed;
        window.RemoveHandler(InputElement.KeyDownEvent, OnKey);
    }
    private void OnClosed(object? sender, EventArgs e) => Dispose();
}
