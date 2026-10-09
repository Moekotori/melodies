using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Controls;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView : UserControl, IDisposable
{
    private readonly AlbumDetailViewModel _model;
    private readonly CancellationTokenSource _coverRequest = new();
    private AlbumArtworkCache.Lease? _lease;
    private bool _closed;
    private readonly LocalizationService? _text;
    public event Action? Back;
    public event Action<string>? ArtistRequested;
    public event Action<string>? GenreRequested;
    public event Action<AlbumRow>? AlbumRequested;
    public event Action<string, bool, int>? PersonalStateChanged;
    public AlbumDetailView() { InitializeComponent(); _model = null!; }
    public AlbumDetailView(AlbumRow album, IAlbumLibrary library, AlbumArtworkCache artwork, PlayerViewModel player, IDetailResources? resources = null, LocalizationService? text = null) : this()
    {
        _library = library; _artwork = artwork; _resources = resources; _text = text;
        _model = new(album, library, player, text);
        DataContext = _model;
        _model.ObservePlayback();
        if (text is not null) text.Changed += OnLanguage;
        _model.PersonalStateChanged += () => PersonalStateChanged?.Invoke(album.Id, _model.Favorite, _model.Rating);
        _model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AlbumDetailViewModel.Rating)) UpdateRating();
            if (args.PropertyName is nameof(AlbumDetailViewModel.Genres) or nameof(AlbumDetailViewModel.DiscCount)
                or nameof(AlbumDetailViewModel.TrackCountText) or nameof(AlbumDetailViewModel.DiscText)) RebuildMeta();
        };
        SizeChanged += (_, _) => UpdateHeroLayout();
        UpdateRating(); RebuildMeta();
        _ = _model.LoadAsync(); _ = LoadCoverAsync(artwork, album.SamplePath);
    }
    private void OnLanguage() => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (!_closed) RebuildMeta(); });
    private async Task LoadCoverAsync(AlbumArtworkCache artwork, string path)
    {
        var lease = await artwork.AcquireAsync(path, _coverRequest.Token, maxEdge: 640);
        if (_closed) { lease?.Dispose(); return; }
        _lease = lease; Cover.Source = lease?.Image;
        CoverPlaceholder.IsVisible = lease?.Image is null;
        CoverButton.IsEnabled = lease?.Image is not null;
    }
    private void OnBack(object? sender, RoutedEventArgs args) => Back?.Invoke();
    private void OnArtist(object? sender, RoutedEventArgs args) => ArtistRequested?.Invoke(_model.Album.Artist);
    private void OnGenre(object? sender, RoutedEventArgs args)
    { if (sender is Button { Tag: string genre } && genre.Length > 0) GenreRequested?.Invoke(genre); }
    private void OnTrackPlay(object? sender, EventArgs args)
    { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem track }) _ = _model.PlayTrackAsync(track.Row); }
    private void OnMenuNext(object? sender, EventArgs args)
    { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem track }) _ = _model.AddTrackAsync(track.Row, true); }
    private void OnMenuAdd(object? sender, EventArgs args)
    { if (sender is AlbumTrackRow { DataContext: AlbumTrackItem track }) _ = _model.AddTrackAsync(track.Row, false); }
    private void OnTab(object? sender, RoutedEventArgs args)
    { if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index)) { _model.ActiveTab = index; _ = LoadPanelAsync(index); } }
    private void OnRating(object? sender, RoutedEventArgs args)
    { if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var value)) _ = _model.SetRatingAsync(value); }
    private void UpdateRating()
    {
        Button[] buttons = [Rate1, Rate2, Rate3, Rate4, Rate5];
        for (var i = 0; i < buttons.Length; i++)
        {
            if (buttons[i].Content is GlyphIcon icon) icon.Kind = _model.Rating > i ? "star-filled" : "star";
            buttons[i].Classes.Set("selected", _model.Rating > i);
            ToolTip.SetTip(buttons[i], $"{i + 1} / 5");
            Avalonia.Automation.AutomationProperties.SetName(buttons[i], $"{i + 1} / 5");
        }
    }
    private void RebuildMeta()
    {
        if (_closed || MetaLine is null || RatingChip is null) return;
        MetaLine.Children.Clear();
        if (_model.HasYear) MetaLine.Children.Add(MetaText(_model.YearText));
        if (_model.HasDiscs) MetaLine.Children.Add(MetaText(_model.DiscText));
        MetaLine.Children.Add(MetaText(_model.TrackCountText));
        FactGenres.Children.Clear();
        foreach (var genre in _model.Genres)
        {
            MetaLine.Children.Add(GenreButton(genre, 15));
            FactGenres.Children.Add(GenreButton(genre, 16));
        }
        MetaLine.Children.Add(RatingChip);
    }
    private TextBlock MetaText(string text) => new()
    {
        Text = text, FontSize = 15, Margin = new(0, 0, 18, 9), VerticalAlignment = VerticalAlignment.Center,
        Foreground = this.TryFindResource("EchoMuted", out var brush) ? brush as IBrush : null
    };
    private Button GenreButton(string genre, double size)
    {
        var button = new Button
        {
            Tag = genre, Classes = { "genre-link" }, Margin = new Avalonia.Thickness(0, 0, 18, 9), Padding = new(0),
            Content = new TextBlock { Text = genre, FontSize = size, Foreground = this.TryFindResource("EchoMuted", out var brush) ? brush as IBrush : null }
        };
        button.Click += OnGenre;
        return button;
    }
    private void UpdateHeroLayout()
    {
        if (_model is null || Bounds.Width <= 0) return;
        var top = TopLevel.GetTopLevel(this);
        var viewportWidth = top?.ClientSize.Width ?? Bounds.Width;
        var viewportHeight = top?.ClientSize.Height ?? 1000;
        var compact = viewportWidth >= 981 && viewportHeight <= 900;
        var narrow = viewportWidth <= 980 || Bounds.Width < 720;
        var medium = !narrow && (viewportWidth <= 1260 || Bounds.Width < coverBudget(viewportWidth, compact) + 360 + (compact ? 200 : 240) + 64);
        var cover = narrow ? Math.Min(320, Math.Max(160, Bounds.Width * .78)) : Math.Min(coverBudget(viewportWidth, compact), Math.Max(160, Bounds.Width - 48));
        var factsWidth = compact ? 200 : 240;
        var beside = !narrow && !medium;
        Hero.ColumnDefinitions[0].Width = new(cover);
        Hero.ColumnDefinitions[2].Width = new(beside ? factsWidth : 0);
        Hero.ColumnSpacing = narrow ? 20 : compact ? Math.Clamp(viewportWidth * .03, 22, 40) : medium ? 30 : Math.Clamp(viewportWidth * .048, 32, 64);
        Hero.Margin = new(0, compact ? 28 : 34, 0, 0);
        Grid.SetColumn(CoverButton, 0); Grid.SetRow(CoverButton, 0); Grid.SetColumnSpan(CoverButton, narrow ? 3 : 1);
        Grid.SetColumn(Console, narrow ? 0 : 1); Grid.SetRow(Console, narrow ? 1 : 0); Grid.SetColumnSpan(Console, narrow ? 3 : 1);
        Grid.SetColumn(Facts, beside ? 2 : 0); Grid.SetRow(Facts, beside ? 0 : narrow ? 2 : 1); Grid.SetColumnSpan(Facts, beside ? 1 : 3);
        Facts.BorderThickness = beside ? new(1, 0, 0, 0) : new(0, 1, 0, 0);
        Facts.Padding = beside ? new(compact ? 18 : 24, 4, 0, 4) : new(0, 14, 0, 0);
        Facts.IsVisible = true;
        FactsPanel.Orientation = beside ? Orientation.Vertical : Orientation.Horizontal;
        FactsPanel.ItemSpacing = beside ? (compact ? 9 : 18) : 24;
        CoverFrame.Width = cover; CoverFrame.Height = cover;
        Copy.Spacing = compact ? 7 : 14; Console.Spacing = compact ? 12 : 24;
        var button = compact ? 32 : 40;
        PlayButton.Height = PlayButton.MinHeight = button;
        AlbumFavoriteButton.Width = AlbumFavoriteButton.Height = MoreButton.Width = MoreButton.Height = button;
        var units = _model.Album.Title.Trim().EnumerateRunes().Sum(rune => System.Text.Rune.IsWhiteSpace(rune) ? .32 : rune.Value <= 0x24f ? .58 : 1);
        var density = units >= 48 ? 2 : units >= 30 ? 1 : 0;
        var title = TitleSize(viewportWidth, viewportHeight, density);
        AlbumTitle.FontSize = narrow ? Math.Min(title, 48) : title;
        AlbumTitle.LineHeight = AlbumTitle.FontSize * (density == 2 ? 1.08 : density == 1 ? 1.05 : 1.02);
        AlbumTitle.FontWeight = (FontWeight)560;
        var artist = compact ? 16 : Math.Clamp(viewportWidth * .017, 20, 24);
        ArtistLink.FontSize = artist; ArtistLabel.FontSize = artist;
        var compactRows = Bounds.Width < 720;
        TrackHeader.ColumnDefinitions[0].Width = new(compactRows ? 38 : 46);
        TrackHeader.ColumnDefinitions[2].Width = new(compactRows ? 0 : 230);
        TrackHeader.ColumnDefinitions[3].Width = new(compactRows ? 56 : 68);
        TrackHeader.ColumnSpacing = compactRows ? 12 : 18; SignalHeader.IsVisible = !compactRows;
        static double coverBudget(double width, bool shortWindow)
        {
            if (width <= 980) return Math.Min(320, width * .78);
            if (shortWindow) return Math.Max(200, Math.Min(280, width * .22));
            if (width <= 1260) return Math.Max(220, Math.Min(320, width * .30));
            return Math.Max(248, Math.Min(352, width * .24));
        }
        static double TitleSize(double width, double height, int density)
        {
            if (width >= 981 && height <= 900) return density switch { 2 => Math.Clamp(width * .0225, 25, 36), 1 => Math.Clamp(width * .0275, 30, 44), _ => Math.Clamp(width * .033, 34, 52) };
            if (width >= 981) return density switch { 2 => Math.Clamp(width * .029, 32, 48), 1 => Math.Clamp(width * .036, 38, 60), _ => Math.Clamp(width * .045, 46, 76) };
            return density switch { 2 => Math.Clamp(width * .0255, 30, 44), 1 => Math.Clamp(width * .032, 36, 54), _ => Math.Clamp(width * .04, 42, 68) };
        }
    }
    private async void OnCopyFolder(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, _model.Folder); }
    private async void OnCopyInfo(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, $"{_model.Album.Title}\n{_model.Album.Artist}\n{_model.Meta}\n{_model.DurationLabel}"); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; if (_text is not null) _text.Changed -= OnLanguage; _coverRequest.Cancel(); _model.Dispose();
        ReleasePanels();
        ClosePreview();
        Cover.Source = null; _lease?.Dispose(); _lease = null; Back = null; PersonalStateChanged = null; ArtistRequested = null; GenreRequested = null; AlbumRequested = null;
    }
}
