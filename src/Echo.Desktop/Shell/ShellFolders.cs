using Echo.Contracts.Library;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public IFolderLibrary? FolderLibrary => AlbumLibrary as IFolderLibrary;
    // Lightweight navigation state only; pages and rows are released when leaving.
    public string? LastFolderPath { get; set; }
}
