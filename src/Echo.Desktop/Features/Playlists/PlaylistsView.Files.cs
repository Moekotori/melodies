using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Echo.Desktop.Features.Playlists;

public partial class PlaylistsView
{
    private static readonly FilePickerFileType[] FileTypes = new[] { "m3u8", "m3u", "pls", "json", "csv", "xspf", "wpl", "txt" }
        .Select(format => new FilePickerFileType(format.ToUpperInvariant()) { Patterns = ["*." + format] }).ToArray();
    private PlaylistRecoveryView? recovery;

    private async void OnImportFile(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || !Model.CanTransfer) return;
        PlaylistFilesButton.Flyout?.Hide();
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = Model.Text["playlists.import"], FileTypeFilter = FileTypes, AllowMultiple = false });
            if (closed || files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (path is null) throw new IOException(Model.Text["playlists.localFileRequired"]);
            await using var input = await files[0].OpenReadAsync();
            var bytes = new byte[512 * 1024 + 1]; var length = 0;
            while (length < bytes.Length)
            {
                var read = await input.ReadAsync(bytes.AsMemory(length), Model.FileToken);
                if (read == 0) break; length += read;
            }
            if (length == bytes.Length) throw new IOException(Model.Text["playlists.fileTooLarge"]);
            if (!closed) await Model.ImportFileAsync(path, bytes.AsSpan(0, length).ToArray());
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closed) Model.Error = error.Message; }
    }

    private async void OnExportFile(object? sender, RoutedEventArgs args)
    {
        if (TopLevel.GetTopLevel(this) is not { } top || !Model.CanExport) return;
        DetailMoreButton.Flyout?.Hide();
        try
        {
            var safeName = string.Concat(Model.Title.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var file = await top.StorageProvider.SaveFilePickerAsync(new() { Title = Model.Text["playlists.export"],
                SuggestedFileName = safeName + ".m3u8", DefaultExtension = "m3u8", FileTypeChoices = FileTypes, ShowOverwritePrompt = true });
            if (file is null || closed) return;
            var format = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant();
            var path = file.TryGetLocalPath() ?? throw new IOException(Model.Text["playlists.localFileRequired"]);
            await Model.ExportFileAsync(format, (bytes, token) => SaveFileAsync(path, bytes, token));
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closed) Model.Error = error.Message; }
    }

    private void OnRecovery(object? sender, RoutedEventArgs args)
    {
        if (closed || shell.PlaylistLibrary is null) return;
        PlaylistFilesButton.Flyout?.Hide();
        CloseRecovery();
        recovery = new(new(shell.PlaylistLibrary, Model.Text, () => { if (!closed) _ = Model.LoadBrowserAsync(); }));
        recovery.Close += CloseRecovery; RecoveryHost.Content = recovery; RecoveryHost.IsVisible = true; recovery.Focus();
    }
    private void CloseRecovery()
    {
        recovery?.Dispose(); recovery = null; RecoveryHost.Content = null; RecoveryHost.IsVisible = false;
        if (!closed) PlaylistFilesButton.Focus();
    }

    private static Task SaveFileAsync(string path, byte[] content, CancellationToken token) => Task.Run(async () =>
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var owned = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
            {
                owned = true; await output.WriteAsync(content, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested(); File.Move(temporary, path, true);
        }
        finally { if (owned) File.Delete(temporary); }
    }, token);
}
