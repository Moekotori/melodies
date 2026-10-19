using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Search;

// Fixed source order breaks equal-score ties exactly as before, independently of completion order.
public sealed partial class GlobalSearchViewModel
{
    private readonly GlobalSearchConcurrency concurrency = new();
    private List<Func<CancellationToken, Task<IReadOnlyList<GlobalSearchHit>>>> LibraryQueries(string query)
    {
        var queries = new List<Func<CancellationToken, Task<IReadOnlyList<GlobalSearchHit>>>>(6);
        if (shell.TrackLibrary is { } tracks) queries.Add(async token =>
        {
            var page = await tracks.GetTracksAsync(new(16, query), token);
            return page.Items.Take(16).Select(track => new GlobalSearchHit(shell.Text["navigation.songs"],
                track.Title, track.Artist + " · " + track.Album, "music", track)).ToArray();
        });
        if (query.StartsWith('/')) return queries;
        if (shell.AlbumLibrary is { } albums) queries.Add(async token =>
        {
            var page = await albums.GetAlbumsAsync(new(Limit: 16, Search: query), token);
            return page.Items.Take(16).Select(album => new GlobalSearchHit(shell.Text["navigation.albums"],
                album.Title, album.Artist, "disc", album)).ToArray();
        });
        if (shell.ArtistLibrary is { } artists) queries.Add(async token =>
        {
            var page = await artists.GetArtistsAsync(new(Limit: 16, Search: query), token);
            return page.Items.Take(16).Select(artist => new GlobalSearchHit(shell.Text["navigation.artists"],
                artist.Name, shell.Text.Format("query.artistCount", artist.TrackCount, artist.AlbumCount), "user", artist)).ToArray();
        });
        if (shell.GenreLibrary is { } genres) queries.Add(async token =>
        {
            var page = await genres.GetGenresAsync(new(Limit: 16, Search: query), token);
            return page.Items.Take(16).Select(genre => new GlobalSearchHit(shell.Text["navigation.genres"],
                genre.Name, shell.Text.Format("query.artistCount", genre.TrackCount, genre.AlbumCount), "disc", genre)).ToArray();
        });
        if (shell.PlaylistLibrary is { } playlists) queries.Add(async token =>
        {
            var page = await playlists.GetPlaylistsAsync(new(Limit: 16, Search: query), token);
            return page.Items.Take(16).Select(playlist => new GlobalSearchHit(shell.Text["navigation.playlists"],
                playlist.Name, playlist.Description, "queue", playlist)).ToArray();
        });
        if (shell.FolderLibrary is IFolderWorkbench folders) queries.Add(async token =>
        {
            var page = await folders.SearchFoldersAsync(query, limit: 16, cancellation: token);
            return page.Items.Take(16).Select(folder => new GlobalSearchHit(shell.Text["navigation.folders"],
                folder.Name, folder.Path, "folder", folder)).ToArray();
        });
        return queries;
    }
    private async Task ReadLibrariesAsync(string query, int version, CancellationToken token, IReadOnlyList<GlobalSearchHit> catalog)
    {
        var queries = LibraryQueries(query);
        var buckets = new IReadOnlyList<GlobalSearchHit>?[queries.Count];
        // At most six fixed requests exist; only two can enter the typed Core client at once.
        await Task.WhenAll(Enumerable.Range(0, queries.Count).Select(Read));
        if (Current() && !query.StartsWith('/') && catalog.Count + buckets.Sum(bucket => bucket?.Count ?? 0) < 24)
        {
            var context = await ReadContextsAsync(query, token);
            if (Current()) Publish(catalog.Concat(buckets.SelectMany(bucket => bucket ?? [])).Concat(context), query);
        }
        async Task Read(int index)
        {
            try
            {
                var result = await concurrency.ReadAsync(() => queries[index](token), token);
                if (closed || version != generation || token.IsCancellationRequested) return;
                buckets[index] = result;
                Publish(catalog.Concat(buckets.SelectMany(bucket => bucket ?? [])), query);
            }
            catch (OperationCanceledException) { }
            catch (ArgumentException) { if (Current()) Error = shell.Text["query.invalid"]; }
            catch (Exception) { if (Current()) Error = shell.Text["query.failed"]; }
        }
        bool Current() => !closed && version == generation && !token.IsCancellationRequested;
    }
    private void Publish(IEnumerable<GlobalSearchHit> results, string query)
        => Hits = results.OrderBy(hit => Rank(hit.Title, query)).Take(24)
            .Append(new GlobalSearchHit(shell.Text["globalSearch.setting"], string.Format(Copy.SettingsTitle, query),
                Copy.SettingsHint, "settings", new SearchQueryTarget("settings", query)))
            .GroupBy(hit => hit.Kind).SelectMany(group => group.Select((hit, index) =>
                hit with { SectionTitle = index == 0 ? group.Key : "" })).ToArray();
}
