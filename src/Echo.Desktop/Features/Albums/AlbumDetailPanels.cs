using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView
{
    private AlbumOnlineViewModel? _online;
    private IAlbumLibrary? _library;
    private IDetailResources? _resources;
    private AlbumArtworkCache? _artwork;
    private CancellationTokenSource? _panelRequest;
    private int _panelGeneration;
    private int _loadedPanel = -1;
    private async Task LoadPanelAsync(int tab, bool refresh = false)
    {
        if (_closed || (!refresh && _loadedPanel == tab)) return;
        if (tab is 2 or 3 or 4 && _online is not null) _ = _online.ShowAsync(tab);
        _panelRequest?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(_coverRequest.Token);
        _panelRequest = request; var generation = ++_panelGeneration;
        if (tab != 1) ClearAttachments();
        try
        {
            if (tab == 1 && _resources is not null)
            {
                await LoadGalleryAsync(request.Token, generation);
            }
            else if (tab == 3 && _library is not null)
            {
                Versions.Children.Clear(); VersionsStatus.Text = "正在读取本地同名专辑…";
                var title = _model.Album.Title;
                var page = await _library.GetAlbumsAsync(new(Search: title[..Math.Min(title.Length, 128)]), request.Token);
                if (_closed || generation != _panelGeneration) return;
                var candidates = page.Items.Where(album => album.Id != _model.Album.Id && album.Title.Equals(title, StringComparison.OrdinalIgnoreCase) && album.Artist.Equals(_model.Album.Artist, StringComparison.OrdinalIgnoreCase)).ToArray();
                VersionsStatus.Text = candidates.Length == 0 ? "曲库中没有其他同名专辑。" : "本地同名专辑 · 身份与发行版本尚未确认";
                foreach (var album in candidates.Take(32))
                {
                    var button = new Button { Content = $"{album.Title} · {album.Artist} · {album.Year?.ToString() ?? "—"} · {album.TrackCount} 首", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
                    button.Click += (_, _) => AlbumRequested?.Invoke(album); Versions.Children.Add(button);
                }
                if (page.Total > 200 || candidates.Length > 32) VersionsStatus.Text += " · 结果已限量，请在专辑墙搜索更多。";
            }
            else if (tab == 4 && _resources is not null)
            {
                Description.Text = "正在读取介绍…";
                var text = await _resources.ReadDescriptionAsync(_model.Album.SamplePath, request.Token);
                if (_closed || generation != _panelGeneration) return;
                await LoadRelatedAlbumsAsync(request.Token, generation);
                if (_closed || generation != _panelGeneration) return;
                Description.Text = string.IsNullOrWhiteSpace(text) ? "暂无本地介绍。可在专辑文件夹中放置 album.txt、readme.txt 或 description.txt。" : text;
            }
            if (!_closed && generation == _panelGeneration) _loadedPanel = tab;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (_closed || generation != _panelGeneration) return;
            if (tab == 1) GalleryStatus.Text = "附件读取失败，请确认磁盘已连接且文件夹可访问，再点击刷新。";
            _model.Error = "内容读取失败，请检查文件或曲库，再点击当前标签的刷新按钮。";
        }
        finally { if (ReferenceEquals(_panelRequest, request)) _panelRequest = null; }
    }
    private void OnRefreshPanel(object? sender, RoutedEventArgs args) => _ = LoadPanelAsync(_model.ActiveTab, true);
    private void OnOpenFolder(object? sender, RoutedEventArgs args) => _ = OpenResourceAsync(_model.Folder);
    private async Task OpenResourceAsync(string path)
    {
        try
        {
            if (_resources is null) { _model.Error = "当前运行环境无法打开本地文件。"; return; }
            await _resources.OpenAsync(path, _coverRequest.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) _model.Error = "无法打开文件，请确认文件存在且已安装对应应用。"; }
    }
    private void ReleasePanels() { ClearRelatedAlbums(); _online?.Dispose(); ++_panelGeneration; _panelRequest?.Cancel(); ClearAttachments(); Versions.Children.Clear(); }
    public void SetWindowVisible(bool visible)
    {
        if (_closed) return;
        if (!visible) { ++_panelGeneration; _panelRequest?.Cancel(); ClearAttachments(); _loadedPanel = -1; ClosePreview(); }
        else _ = LoadPanelAsync(_model.ActiveTab);
    }
}
