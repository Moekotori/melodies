using Echo.Contracts.Library;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.History;

public sealed record HistoryMetric(string Label, string Value);
public sealed record HistoryComparison(string Label, string Current, string Previous, string Change);
public sealed record HistoryRankDisplay(string Number, string Name, string Subtitle, string Plays, string Duration);

public sealed class HistoryReviewDisplay
{
    public System.Globalization.CultureInfo Culture { get; }
    public HistoryReview Data { get; }
    public IReadOnlyList<HistoryMetric> Totals { get; }
    public IReadOnlyList<HistoryMetric> Sessions { get; }
    public IReadOnlyList<HistoryMetric> Discovery { get; }
    public IReadOnlyList<HistoryComparison> Comparison { get; }
    public IReadOnlyList<HistoryRankDisplay> Tracks { get; }
    public IReadOnlyList<HistoryRankDisplay> Artists { get; }
    public IReadOnlyList<HistoryRankDisplay> Albums { get; }
    public IReadOnlyList<HistoryRankDisplay> Formats { get; }
    public IReadOnlyList<HistoryRankDisplay> Quality { get; }
    public bool HasComparison => Comparison.Count > 0;
    public HistoryReviewDisplay(HistoryReview data, LocalizationService text)
    {
        Data = data; Culture = text.Current.Culture; var t = data.Totals;
        string Count(long n) => n.ToString("N0", text.Current.Culture);
        string Percent(double n) => n.ToString("P1", text.Current.Culture);
        var listeningTime = t.Seconds >= 3600 ? text.Format("history.durationHours", (long)(t.Seconds / 3600), (long)(t.Seconds % 3600 / 60))
            : text.Format("history.durationMinutes", (long)(t.Seconds / 60));
        Totals = [new(text["history.seconds"], listeningTime), new(text["history.plays"], Count(t.Plays)),
            new(text["history.tracks"], Count(t.Tracks)), new(text["history.artists"], Count(t.Artists))];
        Sessions = [new(text["history.activeDays"], Count(t.ActiveDays)),
            new(text["history.dailyAverage"], HistoryText.Clock(t.ActiveDays == 0 ? 0 : t.Seconds / t.ActiveDays)),
            new(text["history.sessions"], Count(data.Sessions.Count)), new(text["history.sessionAverage"], HistoryText.Clock(data.Sessions.AverageSeconds)),
            new(text["history.longestSession"], HistoryText.Clock(data.Sessions.LongestSeconds)), new(text["history.streak"], Count(data.Sessions.LongestStreak)),
            new(text["history.countedRatio"], Percent(t.Plays == 0 ? 0 : (double)t.Counted / t.Plays))];
        Discovery = [new(text["history.replayed"], Count(data.Discovery.ReplayedTracks)), new(text["history.repeatRate"], Percent(data.Discovery.RepeatRate))];
        if (data.Previous is not null)
            Discovery = new[] { new HistoryMetric(text["history.newTracks"], Count(data.Discovery.NewTracks)),
                new HistoryMetric(text["history.returning"], Count(data.Discovery.ReturningTracks)), new HistoryMetric(text["history.rediscovered"], Count(data.Discovery.RediscoveredTracks)) }.Concat(Discovery).ToArray();
        Comparison = data.Previous is { } p ? new[] { Compare("history.seconds", t.Seconds, p.Seconds, true),
            Compare("history.plays", t.Plays, p.Plays, false), Compare("history.tracks", t.Tracks, p.Tracks, false) } : [];
        Tracks = Ranks(data.Tracks); Artists = Ranks(data.Artists); Albums = Ranks(data.Albums); Formats = Ranks(data.Formats);
        Quality = Ranks(data.Quality);
        HistoryComparison Compare(string key, double current, double previous, bool duration)
            => new(text[key], duration ? HistoryText.Clock(current) : Count((long)current), duration ? HistoryText.Clock(previous) : Count((long)previous),
                previous == 0 ? text["history.noBaseline"] : ((current - previous) / previous).ToString("+0.0%;-0.0%;0.0%", text.Current.Culture));
        IReadOnlyList<HistoryRankDisplay> Ranks(IReadOnlyList<HistoryRank> ranks) => ranks.Select((rank, i) => new HistoryRankDisplay((i + 1).ToString("00"),
            rank.Name.Length == 0 ? text["history.unknown"] : rank.Name, rank.Subtitle, text.Format("history.rankPlays", rank.Plays), HistoryText.Clock(rank.Seconds))).ToArray();
    }
}
