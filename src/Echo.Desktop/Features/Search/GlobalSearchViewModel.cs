using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Search;

// Open-overlay lifetime: 16 DTOs per library kind, 24 visible results, one cancellable request.
public sealed partial class GlobalSearchViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel shell;
    private CancellationTokenSource? request;
    private bool closed;
    private int generation;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isOpening;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private IReadOnlyList<GlobalSearchHit> hits = [];
    public bool IsExpanded => Search.Trim().Length > 0;
    public bool HasError => Error.Length > 0;
    public bool IsEmpty => IsExpanded && !IsLoading && !HasError && Hits.Count == 0;
    public string Summary => shell.Text.Format("globalSearch.count", Hits.Count);
    public GlobalSearchViewModel(ShellViewModel shell)
    { this.shell = shell; shell.Text.Changed += OnLanguage; }
    private void OnLanguage() => _ = LoadAsync();
    partial void OnSearchChanged(string value) { Notify(); _ = LoadAsync(); }
    partial void OnHitsChanged(IReadOnlyList<GlobalSearchHit> value) => Notify();
    partial void OnErrorChanged(string value) => Notify();
    partial void OnIsLoadingChanged(bool value) => Notify();
    private void Notify()
    { OnPropertyChanged(nameof(IsExpanded)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(IsEmpty)); OnPropertyChanged(nameof(Summary)); }

    public async Task LoadAsync()
    {
        if (closed) return;
        request?.Cancel(); using var current = new CancellationTokenSource(); request = current;
        var version = ++generation;
        var query = Search.Trim(); query = query[..Math.Min(128, query.Length)];
        Hits = []; Error = ""; IsLoading = query.Length > 0;
        try
        {
            if (query.Length == 0) return;
            var results = GlobalSearchCatalog.Match(shell, query).ToList();
            Publish();
            await Task.Delay(180, current.Token);
            if (shell.TrackLibrary is { } tracks)
            {
                var page = await tracks.GetTracksAsync(new(16, query), current.Token);
                results.AddRange(page.Items.Take(16).Select(track => new GlobalSearchHit(shell.Text["navigation.songs"],
                    track.Title, track.Artist + " · " + track.Album, "music", track)));
                Publish();
            }
            if (!query.StartsWith('/') && shell.AlbumLibrary is { } albums)
            {
                var page = await albums.GetAlbumsAsync(new(Limit: 16, Search: query), current.Token);
                results.AddRange(page.Items.Take(16).Select(album => new GlobalSearchHit(shell.Text["navigation.albums"],
                    album.Title, album.Artist, "disc", album)));
                Publish();
            }
            if (!query.StartsWith('/') && shell.ArtistLibrary is { } artists)
            {
                var page = await artists.GetArtistsAsync(new(Limit: 16, Search: query), current.Token);
                results.AddRange(page.Items.Take(16).Select(artist => new GlobalSearchHit(shell.Text["navigation.artists"],
                    artist.Name, shell.Text.Format("query.artistCount", artist.TrackCount, artist.AlbumCount), "user", artist)));
                Publish();
            }
            if (!query.StartsWith('/') && shell.GenreLibrary is { } genres)
            {
                var page = await genres.GetGenresAsync(new(Limit: 16, Search: query), current.Token);
                results.AddRange(page.Items.Take(16).Select(genre => new GlobalSearchHit(shell.Text["navigation.genres"],
                    genre.Name, shell.Text.Format("query.artistCount", genre.TrackCount, genre.AlbumCount), "disc", genre)));
                Publish();
            }
            if (!query.StartsWith('/') && shell.PlaylistLibrary is { } playlists)
            {
                var page = await playlists.GetPlaylistsAsync(new(Limit: 16, Search: query), current.Token);
                results.AddRange(page.Items.Take(16).Select(playlist => new GlobalSearchHit(shell.Text["navigation.playlists"],
                    playlist.Name, playlist.Description, "queue", playlist)));
                Publish();
            }
            void Publish()
            {
                if (!closed && version == generation && !current.IsCancellationRequested)
                    Hits = results.OrderBy(hit => Rank(hit.Title, query)).Take(24).ToArray();
            }
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException) { if (!closed && version == generation) Error = shell.Text["query.invalid"]; }
        catch (Exception) { if (!closed && version == generation) Error = shell.Text["query.failed"]; }
        finally { if (!closed && version == generation) { request = null; IsLoading = false; } }
    }
    private static int Rank(string title, string query) => title.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0
        : title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1
        : title.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : 3;
    public void Dispose()
    {
        if (closed) return; closed = true; ++generation; request?.Cancel(); request = null;
        shell.Text.Changed -= OnLanguage; Hits = []; IsLoading = false;
    }
}
