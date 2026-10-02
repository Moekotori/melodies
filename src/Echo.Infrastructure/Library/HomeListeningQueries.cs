using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // SQLite aggregates twelve weeks of history. The UI never receives the event list.
    private static HomeListeningSummary ReadHomeListening(SqliteCommand command, DateOnly today, CancellationToken token)
    {
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        static long Unix(DateOnly date) => new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue))).ToUnixTimeSeconds();
        command.Parameters.Clear(); command.Parameters.AddWithValue("$from", Unix(monday.AddDays(-77)));
        command.Parameters.AddWithValue("$week", Unix(monday)); command.Parameters.AddWithValue("$end", Unix(today.AddDays(1)));
        const string from = " FROM listening_events e JOIN tracks t ON t.id=e.track_id AND t.missing=0 WHERE e.history_epoch=(SELECT epoch FROM history_state WHERE id=1) AND e.started >= $from AND e.started < $end ";
        command.CommandText = "SELECT coalesce(sum(e.started >= $week),0),coalesce(sum(CASE WHEN e.started >= $week THEN e.seconds ELSE 0 END),0),count(*),count(DISTINCT t.id),coalesce(avg(e.seconds),0)" + from;
        long weekPlays, total, distinct; double weekSeconds, average;
        using (var r = command.ExecuteReader())
        { r.Read(); weekPlays = r.GetInt64(0); weekSeconds = r.GetDouble(1); total = r.GetInt64(2); distinct = r.GetInt64(3); average = r.GetDouble(4); }
        var days = new List<ListeningDay>(84);
        command.CommandText = "SELECT date(e.started,'unixepoch','localtime'),count(*),sum(e.seconds)" + from + " GROUP BY 1 ORDER BY 1 LIMIT 84";
        using (var r = command.ExecuteReader())
            while (r.Read()) { token.ThrowIfCancellationRequested(); days.Add(new(r.GetString(0), r.GetInt64(1), r.GetDouble(2))); }
        var artists = new List<ListeningArtist>(5);
        command.CommandText = "SELECT coalesce(nullif(t.artist,''),'未知艺术家'),count(*),sum(e.seconds)" + from + " GROUP BY t.artist ORDER BY count(*) DESC,sum(e.seconds) DESC,t.artist LIMIT 5";
        using (var r = command.ExecuteReader())
            while (r.Read()) { token.ThrowIfCancellationRequested(); artists.Add(new(r.GetString(0), r.GetInt64(1), r.GetDouble(2))); }
        var tracks = new List<ListeningTrack>(6);
        command.CommandText = "SELECT " + TrackRowMapping.AliasedColumns + ",count(*),sum(e.seconds),max(e.started)" + from + " GROUP BY t.id ORDER BY count(*) DESC,sum(e.seconds) DESC,t.id LIMIT 6";
        using (var r = command.ExecuteReader())
            while (r.Read()) { token.ThrowIfCancellationRequested(); tracks.Add(new(TrackRowMapping.Read(r), r.GetInt64(14), r.GetDouble(15), r.GetInt64(16))); }
        var albums = new List<ListeningAlbum>(4);
        command.CommandText = "SELECT " + HomeAlbumColumns + ",ranked.plays,ranked.seconds,ranked.last" + HomeAlbumsFrom +
            " JOIN (SELECT t.album_key,count(*) plays,sum(e.seconds) seconds,max(e.started) last" + from + " GROUP BY t.album_key ORDER BY plays DESC,seconds DESC,t.album_key LIMIT 4) ranked ON ranked.album_key=a.id ORDER BY ranked.plays DESC,ranked.seconds DESC,a.id";
        using (var r = command.ExecuteReader())
            while (r.Read()) { token.ThrowIfCancellationRequested(); albums.Add(new(ReadHomeAlbum(r), r.GetInt64(11), r.GetDouble(12), r.GetInt64(13))); }
        var quality = new List<ListeningQuality>(5);
        command.CommandText = "SELECT upper(t.codec),sum(e.seconds)" + from + " GROUP BY upper(t.codec) ORDER BY sum(e.seconds) DESC,upper(t.codec) LIMIT 5";
        using (var r = command.ExecuteReader())
            while (r.Read()) { token.ThrowIfCancellationRequested(); quality.Add(new(r.GetString(0), r.GetDouble(1))); }
        command.CommandText = "WITH ordered AS (SELECT e.started,e.seconds,lag(e.updated) OVER(ORDER BY e.started,e.id) previous" + from +
            "), marked AS (SELECT *,CASE WHEN previous IS NULL OR started-previous>1800 THEN 1 ELSE 0 END new_session FROM ordered)," +
            "grouped AS (SELECT *,sum(new_session) OVER(ORDER BY started ROWS UNBOUNDED PRECEDING) session FROM marked)," +
            "sessions AS (SELECT sum(seconds) seconds FROM grouped GROUP BY session) SELECT count(*),coalesce(avg(seconds),0) FROM sessions";
        long sessions; double averageSession;
        using (var r = command.ExecuteReader()) { r.Read(); sessions = r.GetInt64(0); averageSession = r.GetDouble(1); }
        return new(weekPlays, weekSeconds, days.Count, average, total == 0 ? 0 : 1 - (double)distinct / total,
            days, artists, tracks, albums, quality, sessions, averageSession);
    }
}
