using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistTrackCard : UserControl
{
    public static readonly StyledProperty<TrackRow?> TrackProperty = AvaloniaProperty.Register<ArtistTrackCard, TrackRow?>(nameof(Track));
    public static readonly StyledProperty<string> SubtitleProperty = AvaloniaProperty.Register<ArtistTrackCard, string>(nameof(Subtitle), "");
    public static readonly StyledProperty<string> DurationProperty = AvaloniaProperty.Register<ArtistTrackCard, string>(nameof(Duration), "");
    public static readonly StyledProperty<bool> IsPlayingProperty = AvaloniaProperty.Register<ArtistTrackCard, bool>(nameof(IsPlaying));
    public TrackRow? Track { get => GetValue(TrackProperty); set => SetValue(TrackProperty, value); }
    public string Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    private CancellationTokenSource? request;
    private AlbumArtworkCache.Lease? artwork;
    private bool attached;
    public event Action<TrackRow>? PlayRequested;
    public event Action<TrackRow>? NextRequested;
    public event Action<TrackRow>? AddRequested;
    public ArtistTrackCard()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { attached = true; LoadArtwork(); };
        DetachedFromVisualTree += (_, _) => { attached = false; Release(); };
        PropertyChanged += (_, args) => { if (attached && args.Property == TrackProperty) LoadArtwork(); };
    }
    private async void LoadArtwork()
    {
        Release();
        if (Track is not { } track || TopLevel.GetTopLevel(this)?.DataContext is not ShellViewModel { AlbumArtwork: { } cache }) return;
        using var current = new CancellationTokenSource(); request = current;
        try
        {
            var lease = await cache.AcquireAsync(track.Path, current.Token, 96);
            if (!attached || current.IsCancellationRequested) { lease?.Dispose(); return; }
            artwork = lease; Cover.Source = lease?.Image;
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally { if (ReferenceEquals(request, current)) request = null; }
    }
    private void Release() { request?.Cancel(); request = null; Cover.Source = null; artwork?.Dispose(); artwork = null; }
    private void OnPlay(object? sender, RoutedEventArgs args) { if (Track is { } track) PlayRequested?.Invoke(track); }
    private void OnNext(object? sender, RoutedEventArgs args) { if (Track is { } track) NextRequested?.Invoke(track); }
    private void OnAdd(object? sender, RoutedEventArgs args) { if (Track is { } track) AddRequested?.Invoke(track); }
}
