using Echo.Contracts.Library;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public IHistoryLibrary? HistoryLibrary => AlbumLibrary as IHistoryLibrary;
}
