using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Queue;

// Shares the existing bounded artwork worker/cache. Every realized view owns one lease.
public sealed class QueueArtwork : Border
{
    public static readonly StyledProperty<string?> FilePathProperty = AvaloniaProperty.Register<QueueArtwork, string?>(nameof(FilePath));
    public static readonly StyledProperty<AlbumArtworkCache?> CacheProperty = AvaloniaProperty.Register<QueueArtwork, AlbumArtworkCache?>(nameof(Cache));
    public static readonly StyledProperty<bool> ShowPlaceholderProperty = AvaloniaProperty.Register<QueueArtwork, bool>(nameof(ShowPlaceholder), true);
    public string? FilePath { get => GetValue(FilePathProperty); set => SetValue(FilePathProperty, value); }
    public AlbumArtworkCache? Cache { get => GetValue(CacheProperty); set => SetValue(CacheProperty, value); }
    public bool ShowPlaceholder { get => GetValue(ShowPlaceholderProperty); set => SetValue(ShowPlaceholderProperty, value); }
    private readonly Image image = new() { Stretch = Stretch.UniformToFill };
    private readonly Image placeholder = new() { Stretch = Stretch.UniformToFill };
    private CancellationTokenSource? request;
    private AlbumArtworkCache.Lease? lease;
    private bool attached;
    public QueueArtwork()
    {
        ClipToBounds = true; CornerRadius = new CornerRadius(6);
        placeholder.Bind(Image.SourceProperty, this.GetResourceObservable("EchoPlaceholderImage"));
        Child = new Grid { Children = { placeholder, image } };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    { base.OnAttachedToVisualTree(e); attached = true; Reload(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    { attached = false; Release(); base.OnDetachedFromVisualTree(e); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ShowPlaceholderProperty) placeholder.IsVisible = ShowPlaceholder;
        if (attached && (change.Property == FilePathProperty || change.Property == CacheProperty)) Reload();
    }
    private void Reload()
    {
        Release();
        if (Cache is null || string.IsNullOrWhiteSpace(FilePath)) return;
        request = new(); _ = LoadAsync(Cache, FilePath, request.Token);
    }
    private async Task LoadAsync(AlbumArtworkCache cache, string path, CancellationToken token)
    {
        var acquired = await cache.AcquireAsync(path, token);
        if (!attached || token.IsCancellationRequested) { acquired?.Dispose(); return; }
        lease = acquired; image.Source = lease?.Image;
    }
    private void Release()
    { request?.Cancel(); request?.Dispose(); request = null; image.Source = null; lease?.Dispose(); lease = null; }
}
