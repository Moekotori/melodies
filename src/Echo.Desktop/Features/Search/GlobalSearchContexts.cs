using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Search;

public sealed partial class GlobalSearchViewModel
{
    private IEnumerable<GlobalSearchHit> QueueMatches(string query)
    {
        if (shell.Player is not { } player) return [];
        return player.Session.Queue.Where(track =>
                (track.Metadata?.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(track.FilePath)).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                (track.Metadata?.GetValueOrDefault("artist") ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(16).Select(track => new GlobalSearchHit(shell.Text["navigation.queue"],
                track.Metadata?.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(track.FilePath),
                track.Metadata?.GetValueOrDefault("artist") ?? "", "queue", track));
    }

    // Secondary contexts fill spare space. Each read uses the same cross-generation concurrency budget.
    private async Task<IReadOnlyList<GlobalSearchHit>> ReadContextsAsync(string query, CancellationToken token)
    {
        var results = new List<GlobalSearchHit>(32);
        if (shell.HistoryLibrary is { } history)
        {
            var page = await concurrency.ReadAsync(() => history.GetHistoryAsync(new(query), limit: 16, cancellation: token), token);
            results.AddRange(page.Items.Take(16).Select(item => new GlobalSearchHit(shell.Text["navigation.history"],
                item.Track.Title, item.Track.Artist, "history", item)));
        }
        if (shell.TrackLibrary is { } favorites)
        {
            var page = await concurrency.ReadAsync(() => favorites.GetTracksAsync(new(16, query, Filter: new(FavoriteOnly: true)), token), token);
            results.AddRange(page.Items.Take(16).Select(track => new GlobalSearchHit(shell.Text.Translate("喜欢"),
                track.Title, track.Artist + " · " + track.Album, "heart", new SearchFavoriteTarget(track))));
        }
        return results;
    }
}
