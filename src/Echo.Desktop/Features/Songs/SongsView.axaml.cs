using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Contracts.Scanning;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Songs;

public partial class SongsView : UserControl, IDisposable
{
    private readonly ShellViewModel? shell;
    private CancellationTokenSource? import;
    private bool closed;
    public SongsViewModel? Model { get; }
    public SongsView() => InitializeComponent();
    public SongsView(ShellViewModel shell) : this()
    {
        this.shell = shell; Model = new(shell.TrackLibrary!, shell.Player, shell.Text, shell.QueryPreference("songs")); DataContext = Model;
        Model.PreferenceChanged += () => { try { shell.RememberQuery(Model.Preference); } catch (ArgumentException) { } };
        AttachedToVisualTree += (_, _) => _ = Model.LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    public void FocusSearch() => SearchField.Focus();
    private void OnSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { _ = Model?.LoadAsync(); args.Handled = true; }
        if (args.Key == Key.Escape && Model is not null) { Model.Search = ""; args.Handled = true; }
    }
    private void OnDoubleTapped(object? sender, TappedEventArgs args) => Model?.PlayCommand.Execute(null);
    private async void OnImport(object? sender, RoutedEventArgs args)
    {
        if (shell?.ImportLibrary is null || import is not null || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new() { AllowMultiple = false, Title = shell.Text["query.import"] });
        if (closed || folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        using var current = new CancellationTokenSource(); import = current; CancelImport.IsVisible = true;
        try
        {
            var progress = new Progress<ScanProgress>(state => { if (!closed) ImportStatus.Text = shell.Text.Format("query.scan", state.Processed, state.Failed); });
            await shell.ImportLibrary(path, progress, current.Token);
            if (!closed) { ImportStatus.Text = ""; await Model!.LoadAsync(); }
        }
        catch (OperationCanceledException) { if (!closed) ImportStatus.Text = shell.Text["query.scanCancelled"]; }
        catch (Exception) { if (!closed) ImportStatus.Text = shell.Text["query.scanFailed"]; }
        finally { import = null; if (!closed) CancelImport.IsVisible = false; }
    }
    private void OnCancelImport(object? sender, RoutedEventArgs args) => import?.Cancel();
    public void Dispose() { if (closed) return; closed = true; import?.Cancel(); Model?.Dispose(); }
}
