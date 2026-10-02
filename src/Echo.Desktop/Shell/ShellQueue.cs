using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Shell;
public sealed partial class ShellViewModel
{
    [ObservableProperty] private bool isQueueDrawerOpen;
    [RelayCommand] private void ToggleQueueDrawer()
    {
        IsQueueDrawerOpen = !IsQueueDrawerOpen;
    }
    [RelayCommand] private void CloseQueueDrawer() => IsQueueDrawerOpen = false;
    [RelayCommand] private void OpenQueuePage() { IsQueueDrawerOpen = false; NavigateCommand.Execute("queue"); }
    [RelayCommand] private void OpenQueueHistory() { IsQueueDrawerOpen = false; NavigateCommand.Execute("history"); }
}
