using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Shell;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumsView : UserControl, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly AlbumsViewModel _model;
    private readonly AlbumWallViewport _viewport;
    private DetailNavigator? _detail;
    private readonly MotionPanes _panes = new();
    private CancellationTokenSource? _import;
    private bool _closed;
    public AlbumsViewModel Model => _model;
    public AlbumWallViewport Viewport => _viewport;
    public AlbumsView() { InitializeComponent(); _shell = null!; _model = null!; _viewport = null!; }

    public AlbumsView(ShellViewModel shell) : this()
    {
        _shell = shell;
        _model = new(shell.AlbumLibrary!, text: shell.Text, preference: shell.QueryPreference("albums"));
        _model.PreferenceChanged += () => { try { shell.RememberQuery(_model.Preference); } catch (ArgumentException) { } };
        DataContext = _model;
        _viewport = new(_model, shell.AlbumArtwork!);
        _viewport.OpenAlbum += OpenDetail;
        ViewportHost.Content = _viewport;
        AttachedToVisualTree += (_, _) => { if (_model.Revision is null) _ = _model.RefreshAsync(); };
        DetachedFromVisualTree += (_, _) => Dispose();
    }

    public void FocusSearch() => SearchField.Focus();
    private void OnRefresh(object? sender, RoutedEventArgs args)
    {
        _viewport.SetSuspended(true); _shell.AlbumArtwork!.ClearUnused();
        _viewport.SetSuspended(false); _ = _model.ReloadAsync();
    }
    private void OnClearSearch(object? sender, RoutedEventArgs args) { _model.Search = ""; FocusSearch(); }
    private void OnSearchKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Enter) { _ = _model.RefreshAsync(); args.Handled = true; } }
    private void OnDensityChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_viewport is null || sender is not ComboBox combo) return;
        _viewport.FixedColumns = combo.SelectedIndex switch { 1 => 4, 2 => 5, 3 => 6, _ => 0 };
        _viewport.ScrollTo(0); _viewport.RequestLayout();
    }

    // The wall keeps its cards until the detail has covered it, and refills before it returns.
    public async void OpenDetail(AlbumRow album)
    {
        _detail?.Dispose();
        _detail = new(_shell);
        _detail.Back += CloseDetail;
        _detail.PersonalStateChanged += _model.UpdatePersonalState;
        _detail.OpenAlbum(album);
        DetailHost.Content = _detail;
        if (await _panes.Show(DetailHost, Wall, MotionDirection.Forward) && !_closed) _viewport.SetSuspended(true);
    }
    private async void CloseDetail()
    {
        var leaving = _detail; _detail = null;
        _viewport.SetSuspended(false);
        var settled = await _panes.Show(Wall, DetailHost, MotionDirection.Back);
        leaving?.Dispose();
        if (settled && !_closed) DetailHost.Content = null;
    }
    private async void OnImport(object? sender, RoutedEventArgs args)
    {
        if (_closed || _import is not null || _shell.ImportLibrary is null || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new() { Title = "选择音乐文件夹", AllowMultiple = false });
        if (_closed || folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        using var cancellation = new CancellationTokenSource();
        _import = cancellation; ScanProgressRow.IsVisible = true; ImportButton.IsEnabled = false;
        ScanText.Text = "正在发现音乐文件…";
        try
        {
            var progress = new Progress<ScanProgress>(value =>
            { if (!_closed && ReferenceEquals(_import, cancellation)) ScanText.Text = $"正在导入 · 已处理 {value.Processed:N0} / 发现 {value.Discovered:N0} · 失败 {value.Failed:N0}"; });
            var report = await _shell.ImportLibrary(path, progress, cancellation.Token);
            if (_closed) return;
            _shell.Notice = report.Status switch
            {
                ScanStatus.Cancelled => "已取消导入，已入库的曲目已保留。",
                ScanStatus.Failed => $"导入未完成 · 已新增 {report.Inserted:N0} 首 · 可检查文件夹后重试",
                _ => $"导入结束 · 新增 {report.Inserted:N0} 首 · 失败 {report.Failed:N0} 首"
            };
            await _model.RefreshAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) _model.Error = "导入失败。请检查文件夹与曲库，再重试。"; }
        finally { _import = null; if (!_closed) { ScanProgressRow.IsVisible = false; ImportButton.IsEnabled = true; } }
    }
    private void OnCancelImport(object? sender, RoutedEventArgs args) => _import?.Cancel();
    public void SetWindowVisible(bool visible) { _viewport.SetSuspended(!visible || _detail is not null); _detail?.SetWindowVisible(visible); }
    public void Dispose()
    { if (_closed) return; _closed = true; _import?.Cancel(); _panes.Dispose(); _detail?.Dispose(); _viewport.Dispose(); _model.Dispose(); }
}
