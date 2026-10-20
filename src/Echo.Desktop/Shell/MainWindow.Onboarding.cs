using Avalonia.Controls;
using Echo.Desktop.Features.Onboarding;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private OnboardingController? onboarding;
    internal void AttachOnboarding(ShellViewModel shell) => onboarding = new(this, shell);
    internal void StartOnboardingIfNeeded() => onboarding?.StartIfNeeded();
    internal void ReleaseOnboarding() { onboarding?.Dispose(); onboarding = null; }
    internal void SetOnboardingView(Control? view)
    {
        if (_surface is null) return;
        var host = this.FindControl<ContentControl>("OnboardingHost")!;
        host.Content = view; host.IsVisible = view is not null;
    }
}
