using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Media;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Artists;

public partial class ArtistCard : UserControl, IDisposable
{
    private ArtistRow? _artist;
    private bool _bound, _useCover;
    private CancellationTokenSource? _request;
    private AlbumArtworkCache.Lease? _lease;
    public bool HasArtworkResult { get; private set; }
    public event Action<ArtistRow>? Open;
    public event Action<int, Key>? MoveFocusRequested;
    public ArtistCard()
    {
        InitializeComponent();
        OpenButton.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)) return;
            MoveFocusRequested?.Invoke(OpenButton.TabIndex, args.Key); args.Handled = true;
        };
    }
    public bool FocusCard() => OpenButton.IsEnabled && OpenButton.Focus();
    public void Bind(ArtistRow? artist, bool useCover, AlbumArtworkCache artwork, int index)
    {
        OpenButton.TabIndex = index;
        if (_bound && _artist == artist && _useCover == useCover) return;
        Release(); HasArtworkResult = false; _bound = true; _artist = artist; _useCover = useCover;
        ArtistName.Text = artist?.Name ?? "正在读取…";
        Stats.Text = artist is null ? "" : $"{artist.TrackCount:N0} 首 · {artist.AlbumCount:N0} 张专辑";
        Monogram.Text = artist is null ? "" : ArtistMonogram.Wall(artist.Name);
        var cjk = artist is not null && ArtistMonogram.IsCjk(artist.Name);
        Monogram.FontFamily = cjk ? (FontFamily)this.FindResource("EchoFont")! : new FontFamily("Times New Roman");
        Monogram.FontWeight = cjk ? FontWeight.Medium : FontWeight.Normal;
        Monogram.FontSize = cjk ? 44 : 58;
        OpenButton.IsEnabled = artist is not null;
        AutomationProperties.SetName(OpenButton, artist is null ? "正在读取艺术家" : $"{artist.Name}，{artist.TrackCount} 首，{artist.AlbumCount} 张专辑");
        ToolTip.SetTip(OpenButton, artist?.Name);
        if (artist is not null) { _request = new(); _ = LoadAsync(artwork, artist, useCover, _request); }
    }
    private async Task LoadAsync(AlbumArtworkCache artwork, ArtistRow artist, bool useCover, CancellationTokenSource request)
    {
        try
        {
            var lease = artist.PortraitPath is { } portraitPath ? await artwork.AcquireArtistAsync(portraitPath, request.Token) : null;
            if (lease?.Image is null && useCover && !request.IsCancellationRequested)
            { lease?.Dispose(); lease = await artwork.AcquireAsync(artist.SamplePath, request.Token); }
            if (request.IsCancellationRequested || _artist != artist || _useCover != useCover) { lease?.Dispose(); return; }
            _lease = lease; Portrait.Source = lease?.Image;
            PortraitFrame.IsVisible = lease?.Image is not null; Monogram.IsVisible = lease?.Image is null;
            HasArtworkResult = true;
        }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    private void OnOpen(object? sender, RoutedEventArgs args) { if (_artist is { } artist) Open?.Invoke(artist); }
    private void Release()
    { _request?.Cancel(); _request = null; Portrait.Source = null; PortraitFrame.IsVisible = false; Monogram.IsVisible = true; _lease?.Dispose(); _lease = null; }
    public void Reset() { Release(); _artist = null; _bound = false; HasArtworkResult = false; OpenButton.IsEnabled = false; }
    public void Dispose() { Reset(); Open = null; MoveFocusRequested = null; }
}
