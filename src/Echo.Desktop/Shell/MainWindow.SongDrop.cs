using Avalonia.Input;
using Avalonia.Platform.Storage;
using Echo.Contracts.Scanning;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private void InitializeSongDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnSongDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnSongDragLeave);
        AddHandler(DragDrop.DropEvent, OnSongDrop);
        Closed += (_, _) => _viewModel?.CancelSongImportCommand.Execute(null);
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible && _viewModel is { } shell) shell.IsSongDropHover = false;
        };
    }

    private static bool CanImport(IStorageItem item) => item is IStorageFolder ||
        item is IStorageFile && ScanFormats.Supports(Path.GetExtension(item.Name));

    private bool CanReceive(DragEventArgs args) => _viewModel is { ImportLibrary: not null, IsSongImportActive: false } &&
        args.DataTransfer.Items.Count <= ShellViewModel.SongDropLimit &&
        (args.DragEffects & DragDropEffects.Copy) != 0 && args.DataTransfer.Contains(DataFormat.File);

    private void OnSongDragOver(object? sender, DragEventArgs args)
    {
        var accepted = CanReceive(args) && args.DataTransfer.TryGetFiles()?.Any(item =>
            CanImport(item) && item.TryGetLocalPath() is { Length: <= 4096 }) == true;
        args.DragEffects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        if (_viewModel is { } shell) shell.IsSongDropHover = accepted;
        args.Handled = true;
    }

    private void OnSongDragLeave(object? sender, DragEventArgs args)
    { if (_viewModel is { } shell) shell.IsSongDropHover = false; }

    private async void OnSongDrop(object? sender, DragEventArgs args)
    {
        if (_viewModel is not { } shell) return;
        shell.IsSongDropHover = false;
        args.Handled = true;
        if (!CanReceive(args)) return;
        var items = args.DataTransfer.TryGetFiles();
        if (items is null) return;
        if (items.Length > ShellViewModel.SongDropLimit) { shell.Notice = shell.Text["import.tooMany"]; return; }
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var skipped = 0;
        foreach (var item in items)
        {
            if (!CanImport(item) || item.TryGetLocalPath() is not { Length: <= 4096 } path) { skipped++; continue; }
            if (seen.Add(path)) paths.Add(path);
        }
        args.DragEffects = paths.Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        await shell.ImportDroppedPathsAsync(paths, skipped);
    }

    private void RefreshImportedSongs()
    {
        if (IsVisible && _viewModel?.ActiveRouteId is "songs" or "albums" or "artists" or "folders" or "search" or "genres") ShowPage();
    }
}
