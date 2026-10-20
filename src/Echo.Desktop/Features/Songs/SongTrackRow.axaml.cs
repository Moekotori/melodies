using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Features.Songs;

// Only realized rows rent thumbnails. Recycle/detach cancels requests and releases the lease.
public partial class SongTrackRow : UserControl
{
    public static readonly StyledProperty<AlbumArtworkCache?> ArtworkProperty = AvaloniaProperty.Register<SongTrackRow, AlbumArtworkCache?>(nameof(Artwork));
    public static readonly StyledProperty<PlayerViewModel?> PlayerProperty = AvaloniaProperty.Register<SongTrackRow, PlayerViewModel?>(nameof(Player));
    public AlbumArtworkCache? Artwork { get => GetValue(ArtworkProperty); set => SetValue(ArtworkProperty, value); }
    public PlayerViewModel? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    private AlbumArtworkCache.Lease? lease;
    private IDisposable? placeholderBinding;
    private CancellationTokenSource? request;
    private PlayerViewModel? observing;
    private int generation;
    private string subtitleArtist = "", subtitleJoined = "", specInline = "";
    public SongTrackRow()
    {
        InitializeComponent(); DataContextChanged += (_, _) => UpdateRow();
        AttachedToVisualTree += (_, _) => { Observe(); UpdateRow(); };
        DetachedFromVisualTree += (_, _) => { Observe(null); Release(false); };
        SizeChanged += (_, _) => ApplyLayout();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ArtworkProperty && this.IsAttachedToVisualTree() && Layout != "dense") _ = LoadCoverAsync();
        if (change.Property == PlayerProperty && this.IsAttachedToVisualTree()) { Observe(); UpdatePlaying(); }
        if (change.Property == LayoutProperty) { ApplyLayout(); if (Layout == "dense") Release(false); else if (this.IsAttachedToVisualTree() && DataContext is TrackRow) _ = LoadCoverAsync(); }
    }
    private void Observe() => Observe(Player);
    private void Observe(PlayerViewModel? player)
    {
        if (ReferenceEquals(observing, player)) return;
        if (observing is not null) observing.PropertyChanged -= OnPlayerChanged;
        observing = player;
        if (observing is not null) observing.PropertyChanged += OnPlayerChanged;
    }
    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PlayerViewModel.CurrentFilePath)) UpdatePlaying(); }
    private void UpdatePlaying() => Classes.Set("playing", DataContext is TrackRow row && Player?.Session.Snapshot.Current?.TrackId == row.Id);
    private void UpdateRow()
    {
        if (DataContext is not TrackRow row) { Release(); return; }
        subtitleArtist = row.Artist; subtitleJoined = string.Join(" · ", new[] { row.Artist, row.Album }.Where(value => !string.IsNullOrWhiteSpace(value)));
        FormatText.Text = row.Codec.ToUpperInvariant(); FormatTag.IsVisible = row.Codec.Length > 0;
        var rate = row.SampleRate >= 1000000 ? $"{row.SampleRate / 1000000d:0.##} MHz" : $"{row.SampleRate / 1000d:0.#} kHz";
        SpecText.Text = row.BitDepth > 0 ? $"{row.BitDepth} bit / {rate}" : rate;
        SpecTag.IsVisible = row.SampleRate > 0;
        BpmTag.IsVisible = row.Source?.Bpm is > 0 && double.IsFinite(row.Source.Bpm.Value);
        BpmText.Text = BpmTag.IsVisible ? row.Source!.Bpm!.Value.ToString("0.#", CultureInfo.InvariantCulture) + " BPM" : "";
        QualityFormatText.Text = FormatText.Text; QualityFormat.IsVisible = FormatTag.IsVisible;
        QualitySpec.Text = SpecText.Text; QualitySpec.IsVisible = SpecTag.IsVisible;
        QualityHiRes.IsVisible = row.BitDepth > 16 || row.SampleRate > 48000;
        ArtistColumn.Text = row.Artist; AlbumColumn.Text = row.Album;
        specInline = string.Join(" · ", new[] { FormatTag.IsVisible ? FormatText.Text : "", SpecTag.IsVisible ? SpecText.Text : "" }.Where(part => part.Length > 0));
        Duration.Text = row.DurationSeconds > 0 && double.IsFinite(row.DurationSeconds)
            ? (string)SongDurationConverter.Instance.Convert(row.DurationSeconds, typeof(string), null, CultureInfo.InvariantCulture) : "--:--";
        Heart.Kind = row.Favorite ? "heart-filled" : "heart"; FavoriteButton.Classes.Set("liked", row.Favorite);
        UpdatePlaying(); ApplyLayout();
        if (Layout == "dense") { Release(false); return; }
        if (this.IsAttachedToVisualTree()) _ = LoadCoverAsync();
    }
    private async Task LoadCoverAsync()
    {
        Release();
        if (DataContext is not TrackRow row || Artwork is not { } artwork) return;
        using var current = new CancellationTokenSource(); request = current; var version = generation;
        try
        {
            var acquired = await artwork.AcquireAsync(row.Source?.AudioPath ?? row.Path, current.Token, Layout == "split" ? 128 : 192);
            if (current.IsCancellationRequested || generation != version || !this.IsAttachedToVisualTree()) { acquired?.Dispose(); return; }
            lease = acquired;
            if (lease?.Image is { } image)
            { placeholderBinding?.Dispose(); placeholderBinding = null; Cover.Source = image; }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally { if (ReferenceEquals(request, current)) request = null; }
    }
    private void Release(bool showPlaceholder = true)
    {
        ++generation; request?.Cancel(); request = null;
        placeholderBinding?.Dispose(); placeholderBinding = null; Cover.Source = null;
        lease?.Dispose(); lease = null;
        if (showPlaceholder) placeholderBinding = Cover.Bind(Image.SourceProperty, this.GetResourceObservable("EchoPlaceholderImage"));
    }
    private SongsViewModel? Select()
    {
        var model = this.FindAncestorOfType<SongsView>()?.Model;
        if (model is not null && DataContext is TrackRow row) model.Selected = row;
        return model;
    }
    private void OnPlay(object? sender, RoutedEventArgs e) { e.Handled = true; Select()?.PlayCommand.Execute(null); }
    private void OnArtistTap(object? sender, TappedEventArgs e)
    { e.Handled = true; if (DataContext is TrackRow { Artist.Length: > 0 } row) this.FindAncestorOfType<SongsView>()?.OpenArtist(row); }
    private void OnAlbumTap(object? sender, TappedEventArgs e)
    { e.Handled = true; if (DataContext is TrackRow { Album.Length: > 0 } row) this.FindAncestorOfType<SongsView>()?.OpenAlbum(row); }
    private void OnFavorite(object? sender, RoutedEventArgs e) { e.Handled = true; Select()?.FavoriteCommand.Execute(null); }
    private void OnMore(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is TrackRow row && sender is Control anchor)
            this.FindAncestorOfType<SongsView>()?.ShowTrackMenu(row, anchor);
    }
}
