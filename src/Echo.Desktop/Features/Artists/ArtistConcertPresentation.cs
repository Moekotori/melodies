using Echo.Contracts.Library;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Artists;

public sealed record ArtistConcertItem(string Day, string Month, string Weekday, string Venue, string Detail, string Time, string Source, string Url);

public sealed partial class ArtistOverviewViewModel
{
    public IReadOnlyList<ArtistConcertItem> Concerts => (Information?.Events ?? []).Take(12).Select(item =>
    {
        var date = DateOnly.TryParseExact(item.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) ? day : (DateOnly?)null;
        var venue = item.Venue ?? item.Region ?? item.Country ?? text["artist.concert.venuePending"];
        var detail = string.Join(" / ", new[] { item.Name, item.Region, item.Country }.OfType<string>().Where(value => value != venue).Distinct());
        var time = item.Time is { } start ? start + (item.Timezone == "Asia/Tokyo" ? " JST" : "") : text["artist.concert.timePending"];
        return new ArtistConcertItem(date?.Day.ToString("00") ?? "—", date?.ToString("yyyy MMM", text.Current.Culture) ?? item.Date,
            date?.ToString("ddd", text.Current.Culture) ?? "", venue, detail, time, item.Source, item.TicketUrl ?? item.Url);
    }).ToArray();
    public string ConcertFailures => Information?.EventFailures is { Count: > 0 } failures
        ? text.Format("artist.concert.partialUnavailable", string.Join(" / ", failures)) : "";
    public bool EventsAreStale => Information?.EventStatus == "stale";
    public bool CanRefreshConcerts => !IsOnlineLoading && !IsLoading;
    public string ConcertHeading => HasEvents ? text.Format("artist.concert.count", Information!.Events.Count) : text["artist.concert.heading"];
    public string ConcertRefresh => text[IsOnlineLoading ? "artist.concert.checking" : "artist.concert.refresh"];
    [RelayCommand] private async Task RefreshConcertsAsync()
    {
        if (CanRefreshConcerts && !lifetime.IsCancellationRequested) await LoadOnlineAsync(lifetime.Token);
    }
}
