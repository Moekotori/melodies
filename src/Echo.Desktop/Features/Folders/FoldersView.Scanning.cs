using Avalonia.Interactivity;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Echo.Contracts.Scanning;

namespace Echo.Desktop.Features.Folders;

public partial class FoldersView
{
    private CancellationTokenSource? _scan;
    private async void OnImport(object? sender, RoutedEventArgs args)
    {
        if (_scan is not null || _shell.ImportLibrary is null || Avalonia.Controls.TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var folders = await storage.OpenFolderPickerAsync(new() { Title = Model.Text["folders.import"], AllowMultiple = false });
            if (_closed) return;
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) await ScanAsync(path);
        }
        catch (Exception) { if (!_closed) Model.Error = Model.Text["folders.scanError"]; }
    }
    private void OnRescan(object? sender, RoutedEventArgs args)
    { if (Model.CurrentFolder is { } folder) _ = ScanAsync(folder.Path); }
    private async void OnCancelScan(object? sender, RoutedEventArgs args)
    { if (_shell.AlbumLibrary is Echo.Core.Contracts.ICoreTaskControl control) await control.CancelActiveTaskAsync(); _scan?.Cancel(); CancelScanButton.IsEnabled = false; ScanText.Text = Model.Text["folders.cancelling"]; }
    private async Task ScanAsync(string path)
    {
        if (_closed || _scan is not null || _shell.ImportLibrary is null) return;
        using var scan = new CancellationTokenSource(); _scan = scan;
        ImportButton.IsEnabled = false; RescanButton.IsEnabled = false; ScanRow.IsVisible = true; CancelScanButton.IsEnabled = true;
        ScanText.Text = Model.Text["folders.scanning"];
        var progress = new Progress<ScanProgress>(state =>
        {
            if (!_closed && !scan.IsCancellationRequested)
                ScanText.Text = Model.Text.Format("folders.scanProgress", state.Processed, state.Discovered, state.Failed);
        });
        try
        {
            var report = await _shell.ImportLibrary(path, progress, scan.Token);
            if (_closed) return;
            ScanText.Text = report.Status switch
            {
                ScanStatus.Cancelled => Model.Text["folders.cancelled"],
                ScanStatus.Completed => Model.Text.Format("folders.scanCompleted", report.Inserted, report.Updated, report.Failed),
                _ => Model.Text["folders.scanError"]
            };
            CancelScanButton.IsEnabled = false;
            await Model.Wall.RefreshAsync();
            if (!_closed)
            {
                Model.SelectBreadcrumb(new(path, System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path)), 0, 0, 0));
            }
        }
        catch (OperationCanceledException) { if (!_closed) ScanText.Text = Model.Text["folders.cancelled"]; }
        catch (Exception) { if (!_closed) Model.Error = Model.Text["folders.scanError"]; }
        finally
        {
            _scan = null;
            if (!_closed) { ImportButton.IsEnabled = true; RescanButton.IsEnabled = Model.HasFolder; CancelScanButton.IsEnabled = false; }
        }
    }
}
