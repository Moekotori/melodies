using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;

namespace Echo.Desktop.Features.Songs;

// Split layout only. Album mates come from the current page, at most eight, with no extra query.
public partial class SongsTrackInspector : UserControl
{
    public static readonly StyledProperty<AlbumArtworkCache?> ArtworkProperty =
        AvaloniaProperty.Register<SongsTrackInspector, AlbumArtworkCache?>(nameof(Artwork));
    public AlbumArtworkCache? Artwork { get => GetValue(ArtworkProperty); set => SetValue(ArtworkProperty, value); }
    private SongsViewModel? watching;
    private AlbumArtworkCache.Lease? lease;
    private IDisposable? placeholderBinding;
    private CancellationTokenSource? request;
    private int generation;

    public SongsTrackInspector()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as SongsViewModel);
        DetachedFromVisualTree += (_, _) => { Watch(null); Release(); };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) { if (IsVisible) ShowSelection(); else Release(); }
        if (change.Property == ArtworkProperty && IsVisible) ShowSelection();
    }
    private void Watch(SongsViewModel? next)
    {
        if (ReferenceEquals(watching, next)) return;
        if (watching is not null) watching.PropertyChanged -= OnModelChanged;
        watching = next;
        if (watching is not null) watching.PropertyChanged += OnModelChanged;
        ShowSelection();
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(SongsViewModel.Selected)) ShowSelection(); }
    private void ShowSelection()
    {
        if (EmptyText is null) return;
        if (!IsVisible || watching?.Selected is not { } row)
        {
            if (EmptyText is not null) EmptyText.IsVisible = true;
            if (Details is not null) Details.IsVisible = false;
            Release();
            return;
        }
        EmptyText.IsVisible = false; Details.IsVisible = true;
        TitleText.Text = row.Title; ArtistText.Text = row.Artist;
        AlbumValue.Text = string.IsNullOrWhiteSpace(row.Album) ? "—" : row.Album;
        SpecValue.Text = Spec(row); DurationValue.Text = Duration(row.DurationSeconds);
        var genre = row.Genre.Trim(); GenreRow.IsVisible = genre.Length > 0; GenreValue.Text = genre;
        RenderAlbum(row); _ = LoadCoverAsync(row);
    }
    private void RenderAlbum(TrackRow current)
    {
        while (AlbumHost.Children.Count > 0)
        {
            if (AlbumHost.Children[0] is Button previous) previous.Click -= OnAlbumPlay;
            AlbumHost.Children.RemoveAt(0);
        }
        var mates = watching!.Items.Where(row => row.Album == current.Album && row.Artist == current.Artist && current.Album.Length > 0).Take(8).ToArray();
        AlbumSection.IsVisible = mates.Length > 1;
        foreach (var row in mates)
        {
            var button = new Button { Tag = row, Classes = { "albumMate" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            if (row.Id == current.Id) button.Classes.Add("current");
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,44") };
            var number = new TextBlock { Text = row.TrackNumber?.ToString(CultureInfo.InvariantCulture) ?? "·", Classes = { "subtle" }, FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var title = new TextBlock { Text = row.Title, FontSize = 13, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var time = new TextBlock { Text = Duration(row.DurationSeconds), Classes = { "muted" }, FontSize = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(title, 1); Grid.SetColumn(time, 2);
            grid.Children.Add(number); grid.Children.Add(title); grid.Children.Add(time);
            button.Content = grid; button.Click += OnAlbumPlay; AlbumHost.Children.Add(button);
        }
    }
    private async Task LoadCoverAsync(TrackRow row)
    {
        Release();
        if (!IsVisible || Artwork is not { } artwork) return;
        using var current = new CancellationTokenSource(); request = current; var version = generation;
        try
        {
            var acquired = await artwork.AcquireAsync(row.Source?.AudioPath ?? row.Path, current.Token, 320);
            if (current.IsCancellationRequested || generation != version || !IsVisible) { acquired?.Dispose(); return; }
            lease = acquired;
            if (lease?.Image is { } image) { placeholderBinding?.Dispose(); placeholderBinding = null; Cover.Source = image; }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally { if (ReferenceEquals(request, current)) request = null; }
    }
    private void Release()
    {
        ++generation; request?.Cancel(); request = null;
        placeholderBinding?.Dispose(); placeholderBinding = null; if (Cover is not null) Cover.Source = null;
        lease?.Dispose(); lease = null;
        if (Cover is not null) placeholderBinding = Cover.Bind(Image.SourceProperty, this.GetResourceObservable("EchoPlaceholderImage"));
    }
    internal void SetChrome(bool stacked)
    {
        Frame.BorderThickness = stacked ? new Thickness(0, 1, 0, 0) : new Thickness(1, 0, 0, 0);
        Frame.Padding = stacked ? new Thickness(0, 14, 0, 8) : new Thickness(18, 0, 4, 8);
    }
    private void OnPlay(object? sender, RoutedEventArgs e) { e.Handled = true; watching?.PlayCommand.Execute(null); }
    private void OnAlbumPlay(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (watching is null || sender is not Button { Tag: TrackRow row }) return;
        watching.Selected = row; watching.PlayCommand.Execute(null);
    }
    private static string Duration(double seconds) => seconds > 0 && double.IsFinite(seconds)
        ? (string)SongDurationConverter.Instance.Convert(seconds, typeof(string), null, CultureInfo.InvariantCulture)! : "--:--";
    private static string Spec(TrackRow row)
    {
        var parts = new List<string>(3);
        if (row.Codec.Length > 0) parts.Add(row.Codec.ToUpperInvariant());
        if (row.SampleRate > 0)
        {
            var rate = row.SampleRate >= 1000000 ? $"{row.SampleRate / 1000000d:0.##} MHz" : $"{row.SampleRate / 1000d:0.#} kHz";
            parts.Add(row.BitDepth > 0 ? $"{row.BitDepth} bit / {rate}" : rate);
        }
        return parts.Count == 0 ? "—" : string.Join(" · ", parts);
    }
}
