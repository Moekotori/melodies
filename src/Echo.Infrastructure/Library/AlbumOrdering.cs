using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

internal static class AlbumOrdering
{
    public static string Sql(AlbumSort field, bool descending)
    {
        var columns = field switch
        {
            AlbumSort.Artist => "i.artist_key{0},i.stable_id{0}",
            AlbumSort.Year => "a.year{0},a.id{0}", AlbumSort.Recent => "a.added{0},a.id{0}",
            AlbumSort.Duration => "a.duration{0},a.id{0}", AlbumSort.TrackCount => "a.track_count{0},a.id{0}",
            AlbumSort.Created => "a.created{0},a.id{0}", AlbumSort.Modified => "a.modified{0},a.id{0}",
            AlbumSort.LastPlayed => "a.last_played{0},a.id{0}", AlbumSort.PlayCount => "a.play_count{0},a.id{0}",
            AlbumSort.Random => "a.shuffle_key{0},a.id{0}", _ => "i.title_key{0},i.stable_id{0}"
        };
        return " ORDER BY " + string.Format(System.Globalization.CultureInfo.InvariantCulture, columns,
            descending && field != AlbumSort.Random ? " DESC" : " ASC");
    }
}
