using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Search;

public sealed record LibrarySearchHit(string Kind, string Title, string Description, object Target);

public sealed partial class LibrarySearchViewModel(ShellViewModel shell) : ObservableObject, IDisposable
{
    private CancellationTokenSource? request;
    private bool closed;
    private int generation;
    [ObservableProperty] private string search = "";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private IReadOnlyList<LibrarySearchHit> hits = [];
    public bool IsEmpty => !IsLoading && Hits.Count == 0 && Error.Length == 0;
    partial void OnSearchChanged(string value) => _ = LoadAsync();
    public async Task LoadAsync()
    {
        if (closed) return;
        request?.Cancel(); using var current = new CancellationTokenSource(); request = current;
        var version = ++generation; Hits = []; Error = ""; IsLoading = true;
        try
        {
            if (string.IsNullOrWhiteSpace(Search)) return;
            await Task.Delay(120, current.Token);
            var results = new List<LibrarySearchHit>(90);
            if (shell.TrackLibrary is { } tracks)
            {
                var page = await tracks.GetTracksAsync(new(30, Search), current.Token);
                results.AddRange(page.Items.Select(track => new LibrarySearchHit(shell.Text["navigation.songs"], track.Title,
                    track.Artist + " · " + track.Album, track)));
            }
            if (!Search.Contains('/') && shell.AlbumLibrary is { } albums)
            {
                var page = await albums.GetAlbumsAsync(new(Limit: 30, Search: Search), current.Token);
                results.AddRange(page.Items.Select(album => new LibrarySearchHit(shell.Text["navigation.albums"], album.Title, album.Artist, album)));
            }
            if (!Search.Contains('/') && shell.ArtistLibrary is { } artists)
            {
                var page = await artists.GetArtistsAsync(new(Limit: 30, Search: Search), current.Token);
                results.AddRange(page.Items.Select(artist => new LibrarySearchHit(shell.Text["navigation.artists"], artist.Name,
                    shell.Text.Format("query.artistCount", artist.TrackCount, artist.AlbumCount), artist)));
            }
            if (!closed && version == generation) Hits = results;
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!closed && version == generation) Error = shell.Text["query.failed"]; }
        finally { if (version == generation) { request = null; IsLoading = false; OnPropertyChanged(nameof(IsEmpty)); } }
    }
    public void Dispose() { if (closed) return; closed = true; ++generation; request?.Cancel(); Hits = []; }
}
