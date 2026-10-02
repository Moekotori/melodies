using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView
{
    private IAlbumLibrary? _library;
    private IDetailResources? _resources;
    private AlbumArtworkCache? _artwork;
    private CancellationTokenSource? _panelRequest;
    private readonly List<AlbumArtworkCache.Lease> _attachments = [];
    private int _panelGeneration;
    private int _loadedPanel = -1;
    private async Task LoadPanelAsync(int tab, bool refresh = false)
    {
        if (_closed || (!refresh && _loadedPanel == tab)) return;
        _panelRequest?.Cancel(); using var request = CancellationTokenSource.CreateLinkedTokenSource(_coverRequest.Token);
        _panelRequest = request; var generation = ++_panelGeneration;
        if (tab != 1) ClearAttachments();
        try
        {
            if (tab == 1 && _resources is not null)
            {
                ClearAttachments(); GalleryStatus.Text = "正在读取附件…";
                var page = await _resources.ReadAttachmentsAsync(_model.Album.SamplePath, request.Token);
                if (_closed || generation != _panelGeneration) return;
                GalleryStatus.Text = page.Items.Count == 0 ? "文件夹中没有图片、文档或文本附件。" : $"{page.Items.Count} 个附件";
                if (page.Truncated) GalleryStatus.Text += " · 仅显示前 32 个附件，可打开文件夹查看全部。";
                foreach (var item in page.Items)
                {
                    request.Token.ThrowIfCancellationRequested();
                    var body = new StackPanel { Spacing = 10, Width = 170 };
                    var image = new Image { Width = 170, Height = 170, Stretch = Avalonia.Media.Stretch.Uniform };
                    if (item.IsImage)
                    {
                        var lease = await _artwork!.AcquireAsync(item.Path, request.Token);
                        if (_closed || generation != _panelGeneration) { lease?.Dispose(); return; }
                        if (lease is not null) { _attachments.Add(lease); image.Source = lease.Image; }
                    }
                    else body.Children.Add(new TextBlock { Text = System.IO.Path.GetExtension(item.Path).ToUpperInvariant(), Height = 170, FontSize = 28 });
                    if (item.IsImage) body.Children.Add(image);
                    body.Children.Add(new TextBlock { Text = item.Name, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
                    var button = new Button { Content = body, Padding = new(0), Margin = new(0, 0, 20, 20) };
                    ToolTip.SetTip(button, $"打开 {item.Name}"); button.Click += async (_, _) => await OpenResourceAsync(item.Path);
                    Attachments.Children.Add(button);
                }
            }
            else if (tab == 3 && _library is not null)
            {
                Versions.Children.Clear(); VersionsStatus.Text = "正在读取本地同名专辑…";
                var title = _model.Album.Title;
                var page = await _library.GetAlbumsAsync(new(Search: title[..Math.Min(title.Length, 128)]), request.Token);
                if (_closed || generation != _panelGeneration) return;
                var candidates = page.Items.Where(album => album.Id != _model.Album.Id && album.Title.Equals(title, StringComparison.OrdinalIgnoreCase)).ToArray();
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
                Description.Text = string.IsNullOrWhiteSpace(text) ? "暂无本地介绍。可在专辑文件夹中放置 album.txt、readme.txt 或 description.txt。" : text;
            }
            if (!_closed && generation == _panelGeneration) _loadedPanel = tab;
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (_closed || generation != _panelGeneration) return;
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
    private void ClearAttachments()
    {
        Attachments.Children.Clear(); foreach (var lease in _attachments) lease.Dispose(); _attachments.Clear();
        if (_loadedPanel == 1) _loadedPanel = -1;
    }
    private void ReleasePanels() { ++_panelGeneration; _panelRequest?.Cancel(); ClearAttachments(); Versions.Children.Clear(); }
    public void SetWindowVisible(bool visible)
    {
        if (_closed) return;
        if (!visible) { ++_panelGeneration; _panelRequest?.Cancel(); ClearAttachments(); _loadedPanel = -1; ClosePreview(); }
        else _ = LoadPanelAsync(_model.ActiveTab);
    }
}
