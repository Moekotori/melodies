using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Automation;
using Avalonia.Media;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Genres;

public partial class GenreCard : UserControl, IDisposable
{
    private GenreRow? _genre;
    public LocalizationService Text { get; set; } = new();
    private bool _bound;
    private string? _language;
    private CancellationTokenSource? _request;
    private AlbumArtworkCache.Lease? _lease;
    public bool HasArtworkResult { get; private set; }
    public event Action<GenreRow>? Open;
    public event Action<int, Key>? MoveFocusRequested;
    public GenreCard()
    {
        InitializeComponent();
        OpenButton.KeyDown += (_, args) =>
        {
            if (args.Key is not (Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End)) return;
            MoveFocusRequested?.Invoke(OpenButton.TabIndex, args.Key); args.Handled = true;
        };
    }
    public bool FocusCard() => OpenButton.IsEnabled && OpenButton.Focus();
    public void Bind(GenreRow? genre, AlbumArtworkCache artwork, int index)
    {
        OpenButton.TabIndex = index;
        if (_bound && _genre == genre && _language == Text.Current.Code) return;
        Release(); HasArtworkResult = false; _bound = true; _genre = genre; _language = Text.Current.Code;
        GenreName.Text = genre is null ? Text["genres.loading"] : genre.Id == "__unclassified__" ? Text["genres.unclassified"] : genre.Name;
        Stats.Text = genre is null ? "" : Text.Format("genres.meta", genre.TrackCount, genre.AlbumCount);
        Monogram.Text = genre is null ? "" : ArtistMonogram.Wall(GenreName.Text ?? genre.Name);
        var cjk = genre is not null && ArtistMonogram.IsCjk(GenreName.Text ?? genre.Name);
        Monogram.FontFamily = cjk ? (FontFamily)this.FindResource("EchoFont")! : new FontFamily("Times New Roman");
        Monogram.FontWeight = cjk ? FontWeight.Medium : FontWeight.Normal;
        Monogram.FontSize = cjk ? 44 : 58;
        OpenButton.IsEnabled = genre is not null;
        AutomationProperties.SetName(OpenButton, genre is null ? Text["genres.loading"] : $"{GenreName.Text}, {Stats.Text}");
        ToolTip.SetTip(OpenButton, GenreName.Text);
        if (genre is not null) { _request = new(); _ = LoadAsync(artwork, genre, _request); }
    }
    private async Task LoadAsync(AlbumArtworkCache artwork, GenreRow genre, CancellationTokenSource request)
    {
        try
        {
            var lease = await artwork.AcquireAsync(genre.SamplePath, request.Token);
            if (request.IsCancellationRequested || _genre != genre) { lease?.Dispose(); return; }
            _lease = lease; Portrait.Source = lease?.Image;
            PortraitFrame.IsVisible = lease?.Image is not null; Monogram.IsVisible = lease?.Image is null;
            HasArtworkResult = true;
        }
        finally { if (ReferenceEquals(_request, request)) _request = null; request.Dispose(); }
    }
    private void OnOpen(object? sender, RoutedEventArgs args) { if (_genre is { } genre) Open?.Invoke(genre); }
    private void Release()
    { _request?.Cancel(); _request = null; Portrait.Source = null; PortraitFrame.IsVisible = false; Monogram.IsVisible = true; _lease?.Dispose(); _lease = null; }
    public void Reset() { Release(); _genre = null; _bound = false; HasArtworkResult = false; OpenButton.IsEnabled = false; }
    public void Dispose() { Reset(); Open = null; MoveFocusRequested = null; }
}
