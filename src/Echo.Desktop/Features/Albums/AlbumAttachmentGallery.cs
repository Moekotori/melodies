using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Echo.Contracts.Library;
using Echo.Desktop.Controls;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView
{
    private readonly List<AlbumArtworkCache.Lease> _attachments = [];
    private AlbumArtworkCache.Lease? _previewLease;
    private async Task LoadGalleryAsync(CancellationToken token, int generation)
    {
        ClearAttachments(); GalleryStatus.Text = "正在扫描专辑附件…";
        var page = await _resources!.ReadAttachmentsAsync(_model.Album.SamplePath, token);
        if (_closed || generation != _panelGeneration) return;
        GalleryStatus.Text = page.Items.Count == 0 ? "没有找到附件。支持歌曲文件夹及 Scans、Booklet、Artwork 等子文件夹中的图片、PDF 和文本。" : $"{page.Items.Count} 个附件 · 点击图片预览，右键可打开文件或所在文件夹";
        if (page.Truncated) GalleryStatus.Text += " · 已限制扫描范围，打开文件夹可查看全部。";
        if (page.Items.Count == 0 && _lease?.Image is { } cover)
        {
            var fallback = new Button { Padding = new(0), Background = Brushes.Transparent,
                Content = new Image { Source = cover, Width = 240, Height = 240, Stretch = Stretch.Uniform } };
            fallback.Click += OnPreviewCover; ToolTip.SetTip(fallback, "专辑封面"); Attachments.Children.Add(fallback);
        }
        foreach (var item in page.Items)
        {
            token.ThrowIfCancellationRequested();
            var body = new StackPanel { Width = 168, Spacing = 0 };
            if (item.IsImage)
            {
                var lease = await _artwork!.AcquireAsync(item.Path, token, maxEdge: 320);
                if (_closed || generation != _panelGeneration) { lease?.Dispose(); return; }
                if (lease is not null) _attachments.Add(lease);
                if (lease?.Image is { } image) body.Children.Add(new Image { Source = image, Width = 168, Height = 126, Stretch = Stretch.UniformToFill });
                else body.Children.Add(DocumentPlaceholder("图片无法预览"));
            }
            else body.Children.Add(DocumentPlaceholder(Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant()));
            var caption = new TextBlock { Text = item.Name, FontSize = 11, Margin = new(10, 9), MaxLines = 2, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
            caption.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("EchoHeading")); body.Children.Add(caption);
            var card = new Border { Child = body, CornerRadius = new(13), ClipToBounds = true, BorderThickness = new(1) };
            card.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("EchoBorder"));
            card.Bind(Border.BackgroundProperty, new DynamicResourceExtension("EchoField"));
            var button = new Button { Content = card, Padding = new(0), Margin = new(0, 0, 12, 12), Background = Brushes.Transparent };
            ToolTip.SetTip(button, item.Name);
            button.Click += async (_, args) => { args.Handled = true; if (item.IsImage) await PreviewAttachmentAsync(item); else await OpenResourceAsync(item.Path); };
            var open = new MenuItem { Header = "打开附件" }; open.Click += async (_, _) => await OpenResourceAsync(item.Path);
            var folder = new MenuItem { Header = "打开所在文件夹" }; folder.Click += async (_, _) => await OpenResourceAsync(Path.GetDirectoryName(item.Path)!);
            var copy = new MenuItem { Header = "复制路径" }; copy.Click += async (_, _) =>
            { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, item.Path); };
            button.ContextMenu = new ContextMenu { ItemsSource = new[] { open, folder, copy } };
            Attachments.Children.Add(button);
        }
    }
    private static Control DocumentPlaceholder(string label)
    {
        var text = new TextBlock { Text = label, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("EchoMuted"));
        return new Border { Child = text, Height = 126 };
    }
    private async Task PreviewAttachmentAsync(AlbumAttachment attachment)
    {
        if (_artwork is null) return;
        var generation = _panelGeneration;
        try
        {
            var lease = await _artwork.AcquireAsync(attachment.Path, _coverRequest.Token, maxEdge: 1024);
            if (_closed || generation != _panelGeneration || _model.ActiveTab != 1) { lease?.Dispose(); return; }
            if (lease?.Image is null) { lease?.Dispose(); _model.Error = "图片无法预览，可右键使用系统应用打开。"; return; }
            ClosePreview(); _previewLease = lease;
            PreviewImage.Source = lease.Image; PreviewTitle.Text = attachment.Name;
            _coverReturnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
            CoverPreview.IsVisible = true; KeyboardNavigation.SetTabNavigation(CoverPreview, KeyboardNavigationMode.Cycle); PreviewClose.Focus();
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) _model.Error = "附件图片读取失败，请刷新后重试。"; }
    }
    private void ClearAttachments()
    {
        if (_previewLease is not null) ClosePreview();
        Attachments.Children.Clear(); foreach (var lease in _attachments) lease.Dispose(); _attachments.Clear();
        if (_loadedPanel == 1) _loadedPanel = -1;
    }
}
