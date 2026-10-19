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
    private LibraryDetails.DetailPageInsetLease? _pageInset;
    private readonly LocalizationService? _text;
    public event Action? Back;
    public event Action<string>? ArtistRequested;
    public event Action<string>? GenreRequested;
    public event Action<AlbumRow>? AlbumRequested;
    public event Action<string, bool, int>? PersonalStateChanged;
    public AlbumDetailView()
    {
        InitializeComponent(); _model = null!;
        AttachedToVisualTree += (_, _) => { _pageInset ??= LibraryDetails.DetailPageInsetLease.Acquire(this); UpdateHeroLayout(); };
        DetachedFromVisualTree += (_, _) => { _pageInset?.Dispose(); _pageInset = null; };
    }
    public AlbumDetailView(AlbumRow album, IAlbumLibrary library, AlbumArtworkCache artwork, PlayerViewModel player, IDetailResources? resources = null, LocalizationService? text = null, Echo.Core.Contracts.IAlbumInformationClient? information = null) : this()
    {
        _library = library; _artwork = artwork; _resources = resources; _text = text;
        _model = new(album, library, player, text);
        _online = new(album, information); OnlinePanel.DataContext = _online;
        DataContext = _model;
        _model.ObservePlayback();
        if (text is not null) text.Changed += OnLanguage;
        _model.PersonalStateChanged += () => PersonalStateChanged?.Invoke(album.Id, _model.Favorite, _model.Rating);
        _model.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(AlbumDetailViewModel.Rating)) UpdateRating();
            if (args.PropertyName is nameof(AlbumDetailViewModel.Genres) or nameof(AlbumDetailViewModel.DiscCount)
                or nameof(AlbumDetailViewModel.TrackCountText) or nameof(AlbumDetailViewModel.DiscText)) { RebuildMeta(); UpdateHeroLayout(); }
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
    { if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index)) { _model.ActiveTab = index; RelatedPanel.IsVisible = index == 4 && RelatedAlbums.Children.Count > 0; _ = LoadPanelAsync(index); } }
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
    private TextBlock MetaText(string text)
    {
        var label = new TextBlock
        {
            Text = text, FontSize = 15, Margin = new(0, 0, 18, 9), VerticalAlignment = VerticalAlignment.Center
        };
        label.Bind(TextBlock.ForegroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("EchoMuted"));
        return label;
    }
    private Button GenreButton(string genre, double size)
    {
        var label = MetaText(genre); label.FontSize = size; label.Margin = new(0);
        var button = new Button
        {
            Tag = genre, Classes = { "genre-link" }, Margin = new Avalonia.Thickness(0, 0, 18, 9),
            Padding = new(0), Content = label
        };
        button.Click += OnGenre;
        return button;
    }
    private async void OnCopyFolder(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, _model.Folder); }
    private async void OnCopyInfo(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, $"{_model.Album.Title}\n{_model.Album.Artist}\n{_model.Meta}\n{_model.DurationLabel}"); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; if (_text is not null) _text.Changed -= OnLanguage; _coverRequest.Cancel(); _model.Dispose();
        _pageInset?.Dispose(); _pageInset = null;
        ReleasePanels();
        ClosePreview();
        Cover.Source = null; _lease?.Dispose(); _lease = null; Back = null; PersonalStateChanged = null; ArtistRequested = null; GenreRequested = null; AlbumRequested = null;
    }
}
