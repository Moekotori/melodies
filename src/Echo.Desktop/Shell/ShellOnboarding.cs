using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    [ObservableProperty] private bool isSetupOpen;
    public event Action<bool>? OnboardingRequested;
    [RelayCommand] private void OpenTutorial() => OnboardingRequested?.Invoke(true);
    [RelayCommand] private void OpenOperationGuide() => OnboardingRequested?.Invoke(false);
}
