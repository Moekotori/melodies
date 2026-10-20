using System.Text.RegularExpressions;
using Echo.Contracts.Library;

namespace Echo.ArtistMetadata;

internal sealed record ArtistConcertResult(IReadOnlyList<ArtistEvent> Events, IReadOnlyList<string> Failures, bool Checked);

internal sealed class ArtistConcertProviders(ArtistHttp http)
{
    internal async Task<ArtistConcertResult?> ReadAsync(string name, CancellationToken token)
    {
        var failures = new List<string>(2); var checkedSource = false;
        try
        {
            var query = Uri.EscapeDataString(name);
            var actors = await http.PageAsync("https://www.eventernote.com/actors/search?keyword=" + query, token).ConfigureAwait(false);
            var actor = ArtistConcertPages.ActorPage(actors, name);
            if (actor is not null)
            {
                var page = await http.PageAsync(actor, token).ConfigureAwait(false); checkedSource = true;
                var events = ArtistConcertPages.Eventernote(page, name, true);
                if (events.Count > 0) return new(events, [], true);
            }
            foreach (var year in new[] { DateTime.UtcNow.Year, DateTime.UtcNow.Year + 1 })
            {
                var page = await http.PageAsync($"https://www.eventernote.com/events/search?keyword={query}&facet=1&limit=30&sort=event_date&order=ASC&year={year}", token).ConfigureAwait(false); checkedSource = true;
                var events = ArtistConcertPages.Eventernote(page, name, false);
                if (events.Count > 0) return new(events, [], true);
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or RegexMatchTimeoutException or OperationCanceledException)
        { token.ThrowIfCancellationRequested(); failures.Add("Eventernote"); }
        try
        {
            var page = await http.PageAsync("https://eplus.jp/sf/search?keyword=" + Uri.EscapeDataString(name), token).ConfigureAwait(false);
            var events = ArtistConcertPages.Eplus(page, name); checkedSource = true;
            return new(events, failures, true);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or RegexMatchTimeoutException or OperationCanceledException)
        { token.ThrowIfCancellationRequested(); failures.Add("eplus"); }
        return new([], failures, checkedSource);
    }
    internal static (IReadOnlyList<ArtistEvent> Events, string Status, IReadOnlyList<string> Failures) Merge(ArtistInformation? music, ArtistConcertResult? pages, ArtistInformation? cached, bool musicFailed)
    {
        var failures = (pages?.Failures ?? new[] { "Eventernote", "eplus" }).ToList();
        if (music is { EventStatus: "error" } || musicFailed) failures.Add("MusicBrainz");
        var fresh = (pages?.Events ?? []).Concat(music?.EventStatus == "ready" ? music.Events : []).ToList();
        var retained = cached?.Events.Where(item => failures.Contains(item.Source) && string.CompareOrdinal(item.Date, DateTime.UtcNow.ToString("yyyy-MM-dd")) >= 0).ToArray() ?? [];
        fresh.AddRange(retained);
        var events = fresh.Where(item => string.CompareOrdinal(item.Date, DateTime.UtcNow.ToString("yyyy-MM-dd")) >= 0)
            .DistinctBy(item => item.Url + "|" + item.Date + "|" + item.Time).OrderBy(item => item.Date).ThenBy(item => item.Time).Take(12).ToArray();
        var success = pages?.Checked == true || music?.EventStatus == "ready" || music is null && !musicFailed;
        return (events, retained.Length > 0 ? "stale" : failures.Count > 0 ? success || events.Length > 0 ? "partial" : "error" : success ? "ready" : "unavailable", failures.Distinct().ToArray());
    }
}
