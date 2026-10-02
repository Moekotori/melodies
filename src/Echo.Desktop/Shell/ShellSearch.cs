using CommunityToolkit.Mvvm.Input;
namespace Echo.Desktop.Shell;
public sealed partial class ShellViewModel
{
    [RelayCommand] private void OpenSearch() => NavigateCommand.Execute("search");
}
