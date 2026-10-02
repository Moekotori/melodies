using Avalonia.Threading;
using Echo.Contracts.Library;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public IHomeLibrary? HomeLibrary => AlbumLibrary as IHomeLibrary;
    public event Action? HomeDataChanged;
    public void NotifyHomeDataChanged() => Dispatcher.UIThread.Post(() => HomeDataChanged?.Invoke());
}
