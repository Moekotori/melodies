using Echo.Contracts.Library;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Artists;

internal static class ArtistOverviewPresentation
{
    internal static IReadOnlyList<ArtistFact> LibraryFacts(ArtistRow artist, ArtistInsights? insights, string sources, LocalizationService text)
    {
        var facts = new List<ArtistFact>(7)
        {
            new(text["artist.content.songs"], artist.TrackCount.ToString("N0", text.Current.Culture)),
            new(text["navigation.albums"], artist.AlbumCount.ToString("N0", text.Current.Culture)),
            new(text["artist.content.duration"], Duration(artist.DurationSeconds, text))
        };
        if (insights?.FirstYear is { } first) facts.Add(new(text["artist.content.years"], insights.LastYear is { } last && first != last ? $"{first}–{last}" : first.ToString()));
        if (insights?.Collection is { CodecTotal: > 0 } collection)
        {
            facts.Add(new(text["artist.content.lossless"], $"{Math.Round(collection.LosslessCount * 100d / collection.CodecTotal)}% · {collection.LosslessCount}/{collection.CodecTotal}"));
            if (collection.TopSpec is { Length: > 0 } spec) facts.Add(new(text["artist.content.topQuality"], spec));
        }
        facts.Add(new(text["artist.content.sources"], sources.Length > 0 ? sources : text["artist.content.localLibrary"]));
        return facts;
    }
    internal static string Duration(double seconds, LocalizationService text)
    {
        var minutes = Math.Max(0, (int)Math.Round(seconds / 60));
        return minutes >= 60 ? text.Format("artist.content.hoursMinutes", minutes / 60, minutes % 60) : text.Format("artist.detail.format.minutes", minutes);
    }
}
