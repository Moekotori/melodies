using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Scanning;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public const int SongDropLimit = 256;
    private CancellationTokenSource? _songImport;
    private bool _songImportReleased;
    [ObservableProperty] private bool isSongDropHover;
    [ObservableProperty] private bool isSongImportActive;
    [ObservableProperty] private string songImportStatus = "";
    public event Action? SongsImported;

    public async Task ImportDroppedPathsAsync(IReadOnlyList<string> paths, int skipped = 0)
    {
        if (_songImportReleased || ImportLibrary is not { } import) return;
        if (IsSongImportActive) { Notice = Text["import.busy"]; return; }
        if (paths.Count > SongDropLimit) { Notice = Text["import.tooMany"]; return; }
        if (paths.Count == 0) { Notice = Text["import.unsupported"]; return; }
        using var current = new CancellationTokenSource();
        _songImport = current; IsSongImportActive = true;
        var added = 0L; var updated = 0L; var unchanged = 0L; var failed = 0L;
        var index = 0;
        try
        {
            if (AlbumLibrary is Echo.Core.Contracts.ICoreBatchImport batch)
            {
                var progress = new Progress<Echo.Core.Contracts.CoreTaskSnapshot>(state =>
                {
                    if (!_songImportReleased && state.ScanProgress is { } scan)
                        SongImportStatus = Text.Format("import.progress", state.InputIndex, state.InputCount, scan.Processed, scan.Failed);
                });
                var report = await batch.ImportBatchAsync(paths, progress, current.Token);
                added = report.Inserted; updated = report.Updated; unchanged = report.Unchanged; failed = report.Failed;
            }
            else for (; index < paths.Count; index++)
            {
                current.Token.ThrowIfCancellationRequested();
                var ordinal = index;
                SongImportStatus = Text.Format("import.progress", index + 1, paths.Count, 0, 0);
                var progress = new Progress<ScanProgress>(value =>
                {
                    if (!_songImportReleased && ReferenceEquals(_songImport, current) && index == ordinal)
                        SongImportStatus = Text.Format("import.progress", ordinal + 1, paths.Count, value.Processed, value.Failed);
                });
                var report = await import(paths[index], progress, current.Token);
                added += report.Inserted; updated += report.Updated; unchanged += report.Unchanged; failed += report.Failed;
                if (report.Status == ScanStatus.Cancelled) throw new OperationCanceledException(current.Token);
                if (report.Status != ScanStatus.Completed) failed++;
            }
            if (!_songImportReleased) Notice = Text.Format("import.completed", added, updated, unchanged, failed, skipped);
        }
        catch (OperationCanceledException) { if (!_songImportReleased) Notice = Text["import.cancelled"]; }
        catch (InvalidOperationException) { if (!_songImportReleased) Notice = Text["import.busy"]; }
        catch (Exception) { if (!_songImportReleased) Notice = Text["query.scanFailed"]; }
        finally
        {
            _songImport = null; IsSongImportActive = false; SongImportStatus = "";
            if (!_songImportReleased) { NotifyHomeDataChanged(); SongsImported?.Invoke(); }
        }
    }

    [RelayCommand] private async Task CancelSongImportAsync()
    {
        if (AlbumLibrary is Echo.Core.Contracts.ICoreTaskControl control) await control.CancelActiveTaskAsync();
        _songImport?.Cancel();
    }
    public void ReleaseSongImport()
    {
        _songImportReleased = true;
        IsSongDropHover = false;
        _songImport?.Cancel();
    }
}
