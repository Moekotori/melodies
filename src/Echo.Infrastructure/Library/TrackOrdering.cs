using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

internal static class TrackOrdering
{
    public static string Sql(IReadOnlyList<TrackOrder>? order)
    {
        order ??= [new(TrackSort.Title)];
        if (order.Count is < 1 or > 3 || order.Any(o => !Enum.IsDefined(o.Field)) || order.Select(o => o.Field).Distinct().Count() != order.Count)
            throw new ArgumentException("Use one to three distinct sorting fields.");
        return " ORDER BY " + string.Join(',', order.SelectMany(o => Columns(o.Field).Select(column => column + (o.Descending ? " DESC" : " ASC")))) + ",t.id ASC";
    }
    private static string[] Columns(TrackSort field) => field switch
    {
        TrackSort.Artist => ["t.artist COLLATE NOCASE"], TrackSort.Album => ["t.album COLLATE NOCASE"],
        TrackSort.TrackNumber => ["t.disc_no", "t.track_no"], TrackSort.Year => ["t.year"],
        TrackSort.Duration => ["t.duration"], TrackSort.SampleRate => ["t.sample_rate"],
        TrackSort.BitDepth => ["t.bit_depth"], TrackSort.Codec => ["t.codec COLLATE NOCASE"],
        TrackSort.Recent => ["t.updated_at"], TrackSort.Modified => ["t.modified_ticks"],
        TrackSort.Created => ["t.created_at"], _ => ["t.sort_key"]
    };
}
