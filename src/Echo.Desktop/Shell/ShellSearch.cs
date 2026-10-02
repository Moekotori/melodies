using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
namespace Echo.Desktop.Shell;
public sealed partial class ShellViewModel
{
    [ObservableProperty] private bool isGlobalSearchOpen;
    [RelayCommand] private void OpenSearch()
    {
        CloseSidebarDrawerCommand.Execute(null);
        CloseQueueDrawerCommand.Execute(null);
        CloseQuickSettingsCommand.Execute(null);
        IsGlobalSearchOpen = true;
    }
    [RelayCommand] private void CloseGlobalSearch() => IsGlobalSearchOpen = false;
    [RelayCommand] private void ToggleGlobalSearch()
    {
        if (IsGlobalSearchOpen) CloseGlobalSearch(); else OpenSearch();
    }
}
