using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Controls;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView : UserControl, IDisposable
{
    private readonly AlbumDetailViewModel _model;
    private readonly CancellationTokenSource _coverRequest = new();
    private AlbumArtworkCache.Lease? _lease;
    private bool _closed;
    public event Action? Back;
    public event Action<string>? ArtistRequested;
    public event Action<AlbumRow>? AlbumRequested;
    public event Action<string, bool, int>? PersonalStateChanged;
    public AlbumDetailView() { InitializeComponent(); _model = null!; }
    public AlbumDetailView(AlbumRow album, IAlbumLibrary library, AlbumArtworkCache artwork, PlayerViewModel player, IDetailResources? resources = null) : this()
    {
        _library = library; _artwork = artwork; _resources = resources;
        _model = new(album, library, player);
        DataContext = _model;
        _model.ObservePlayback();
        _model.PersonalStateChanged += () => PersonalStateChanged?.Invoke(album.Id, _model.Favorite, _model.Rating);
        _model.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AlbumDetailViewModel.Rating)) UpdateRating(); };
        SizeChanged += (_, _) => UpdateHeroLayout();
        UpdateRating();
        _ = _model.LoadAsync(); _ = LoadCoverAsync(artwork, album.SamplePath);
    }
    private async Task LoadCoverAsync(AlbumArtworkCache artwork, string path)
    {
        var lease = await artwork.AcquireAsync(path, _coverRequest.Token, maxEdge: 640);
        if (_closed) { lease?.Dispose(); return; }
        _lease = lease; Cover.Source = lease?.Image; GalleryCover.Source = lease?.Image;
        CoverPlaceholder.IsVisible = lease?.Image is null;
        CoverButton.IsEnabled = lease?.Image is not null;
    }
    private void OnBack(object? sender, RoutedEventArgs args) => Back?.Invoke();
    private void OnArtist(object? sender, RoutedEventArgs args) => ArtistRequested?.Invoke(_model.Album.Artist);
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
    private void UpdateHeroLayout()
    {
        if (_model is null || Bounds.Width <= 0) return;
        var narrow = Bounds.Width < 900;
        var small = Bounds.Width < 650;
        var size = Math.Clamp(Bounds.Width * (narrow ? .27 : .23), 112, 264);
        Hero.ColumnDefinitions[0].Width = new(size); Hero.ColumnDefinitions[2].Width = new(narrow ? 0 : 180);
        Hero.ColumnSpacing = small ? 20 : 32;
        Grid.SetColumn(Facts, narrow ? 0 : 2); Grid.SetRow(Facts, narrow ? 1 : 0); Grid.SetColumnSpan(Facts, narrow ? 3 : 1);
        Facts.BorderThickness = narrow ? new(0, 1, 0, 0) : new(1, 0, 0, 0);
        Facts.Padding = narrow ? new(0, 14, 0, 0) : new(24, 4, 0, 4);
        FactsPanel.Orientation = narrow ? Avalonia.Layout.Orientation.Horizontal : Avalonia.Layout.Orientation.Vertical;
        FactsPanel.ItemSpacing = narrow ? 24 : 13;
        CoverFrame.Width = size; CoverFrame.Height = size;
        var units = _model.Album.Title.Trim().EnumerateRunes().Sum(rune => System.Text.Rune.IsWhiteSpace(rune) ? .32 : rune.Value <= 0x24f ? .58 : 1);
        var density = units >= 48 ? 2 : units >= 30 ? 1 : 0;
        var textWidth = Math.Max(160, Bounds.Width - size - (narrow ? 0 : 180) - Hero.ColumnSpacing * (narrow ? 1 : 2));
        var titleSize = Math.Clamp(textWidth * .085, 30, 44) * (density == 2 ? .8 : density == 1 ? .9 : 1);
        AlbumTitle.FontSize = small ? Math.Min(30, titleSize) : narrow ? Math.Min(36, titleSize) : titleSize;
        AlbumTitle.LineHeight = AlbumTitle.FontSize * 1.18;
        TrackHeader.Margin = new(0, 0, 12, 0);
        var compactRows = Bounds.Width < 650;
        TrackHeader.ColumnDefinitions[0].Width = new(compactRows ? 28 : 46);
        TrackHeader.ColumnDefinitions[2].Width = new(compactRows ? 0 : 230);
        TrackHeader.ColumnDefinitions[3].Width = new(compactRows ? 48 : 68);
        TrackHeader.ColumnSpacing = compactRows ? 8 : 18; SignalHeader.IsVisible = !compactRows;
    }
    private async void OnCopyFolder(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, _model.Folder); }
    private async void OnCopyInfo(object? sender, RoutedEventArgs args)
    { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetValueAsync(DataFormat.Text, $"{_model.Album.Title}\n{_model.Album.Artist}\n{_model.Meta}\n{_model.DurationLabel}"); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _coverRequest.Cancel(); _model.Dispose();
        ReleasePanels();
        ClosePreview();
        Cover.Source = null; GalleryCover.Source = null; _lease?.Dispose(); _lease = null; Back = null; PersonalStateChanged = null; ArtistRequested = null; AlbumRequested = null;
    }
}
