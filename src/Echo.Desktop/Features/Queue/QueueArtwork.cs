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
    private TopLevel? topLevel;
    private int requestedEdge;
    private bool attached;
    public QueueArtwork()
    {
        ClipToBounds = true; CornerRadius = new CornerRadius(6);
        placeholder.Bind(Image.SourceProperty, this.GetResourceObservable("EchoPlaceholderImage"));
        Child = new Grid { Children = { placeholder, image } };
        SizeChanged += (_, _) => RefreshResolution();
        EffectiveViewportChanged += (_, _) => RefreshResolution();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e); attached = true;
        topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not null) topLevel.ScalingChanged += ScalingChanged;
        Reload();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        if (topLevel is not null) topLevel.ScalingChanged -= ScalingChanged;
        topLevel = null; Release(); base.OnDetachedFromVisualTree(e);
    }
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
        requestedEdge = DecodeEdge();
        request = new(); _ = LoadAsync(Cache, FilePath, requestedEdge, request.Token);
    }
    private void ScalingChanged(object? sender, EventArgs e) => RefreshResolution();
    private void RefreshResolution()
    { if (attached && requestedEdge != DecodeEdge()) Reload(); }
    private int DecodeEdge()
    {
        var width = Bounds.Width > 0 ? Bounds.Width : double.IsFinite(Width) ? Width : 64;
        var height = Bounds.Height > 0 ? Bounds.Height : double.IsFinite(Height) ? Height : 64;
        var edge = Math.Max(width, height) * (topLevel?.RenderScaling ?? 1);
        if (topLevel is not null && this.TransformToVisual(topLevel) is { } transform)
            edge *= Math.Max(Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12),
                Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22));
        // Share a few cache sizes; never decode below the physical display size.
        return edge switch { <= 64 => 64, <= 96 => 96, <= 128 => 128, <= 192 => 192,
            <= 256 => 256, <= 320 => 320, <= 512 => 512, <= 768 => 768, _ => 1024 };
    }
    private async Task LoadAsync(AlbumArtworkCache cache, string path, int edge, CancellationToken token)
    {
        var acquired = await cache.AcquireAsync(path, token, edge);
        if (!attached || token.IsCancellationRequested) { acquired?.Dispose(); return; }
        lease = acquired; image.Source = lease?.Image;
    }
    private void Release()
    { request?.Cancel(); request?.Dispose(); request = null; image.Source = null; lease?.Dispose(); lease = null; }
}
