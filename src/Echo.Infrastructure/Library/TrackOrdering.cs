using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

internal static class TrackOrdering
{
    public static string Sql(IReadOnlyList<TrackOrder>? order, int randomSeed = 1)
    {
        order ??= [new(TrackSort.Title)];
        if (order.Count is < 1 or > 21 || order.Any(o => !Enum.IsDefined(o.Field)) || order.Select(o => o.Field).Distinct().Count() != order.Count)
            throw new ArgumentException("Use distinct, supported sorting fields.");
        var clauses = order.SelectMany(o => Clauses(o, Math.Clamp(randomSeed, 1, int.MaxValue))).ToList();
        if (order[0].Field == TrackSort.PlayCount) clauses.Add(FolderTrackSql.LastPlayed + " DESC");
        // Stable ties and a fixed shuffle seed keep offset pages disjoint.
        clauses.AddRange(["t.sort_key ASC", "t.artist COLLATE NOCASE ASC", "t.id ASC"]);
        return " ORDER BY " + string.Join(',', clauses.Distinct());
    }
    private static IEnumerable<string> Clauses(TrackOrder order, int seed)
    {
        var direction = order.Descending ? " DESC" : " ASC";
        string[] columns = order.Field switch
        {
            TrackSort.Artist => ["t.artist COLLATE NOCASE"],
            TrackSort.Album => ["t.album COLLATE NOCASE"],
            TrackSort.ArtistAlbum => ["t.artist COLLATE NOCASE", "t.album COLLATE NOCASE"],
            TrackSort.TrackNumber => ["coalesce(t.disc_no,1)", "coalesce(t.track_no,0)"],
            TrackSort.Year => ["t.year"], TrackSort.Duration => ["t.duration"],
            TrackSort.SampleRate => ["t.sample_rate"], TrackSort.BitDepth => ["t.bit_depth"],
            TrackSort.Codec => ["t.codec COLLATE NOCASE"], TrackSort.Recent => ["t.updated_at"],
            TrackSort.Modified => ["t.modified_ticks"], TrackSort.Created => ["t.created_at"],
            TrackSort.LastPlayed => [FolderTrackSql.LastPlayed], TrackSort.PlayCount => [FolderTrackSql.Plays],
            TrackSort.Bpm => ["coalesce(t.bpm,json_extract(t.source_json,'$.Bpm'),0)"],
            TrackSort.Random => [$"((t.rowid%2147483647)*{seed})%2147483647"],
            TrackSort.AudioSpec => ["t.sample_rate", "t.bit_depth"],
            TrackSort.Quality => [Bitrate, "t.size_bytes"], TrackSort.Bitrate => [Bitrate],
            _ => ["t.sort_key"]
        };
        if (order.Field == TrackSort.Year) yield return "(t.year IS NULL) ASC";
        if (order.Field == TrackSort.TrackNumber) yield return "(t.track_no IS NULL) ASC";
        if (order.Field == TrackSort.Bpm) yield return "(coalesce(t.bpm,json_extract(t.source_json,'$.Bpm'),0)<=0) ASC";
        foreach (var column in columns)
        {
            if (order.Field == TrackSort.AudioSpec) yield return $"({column}<=0) ASC";
            yield return column + direction;
        }
        if (order.Field is TrackSort.Album or TrackSort.ArtistAlbum)
        {
            yield return "coalesce(t.disc_no,0) ASC";
            yield return "coalesce(t.track_no,999999) ASC";
        }
    }
    private const string Bitrate = "coalesce(t.bitrate,0)";
}
