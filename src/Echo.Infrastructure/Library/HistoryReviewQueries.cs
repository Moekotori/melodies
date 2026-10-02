using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // Heavy aggregates use the background read lane, leaving interactive timeline/navigation reads available.
    public Task<HistoryReview> GetHistoryReviewAsync(HistoryFilter filter, CancellationToken cancellation = default)
        => _scanReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            var epoch = ReadHistoryEpoch(connection, transaction);
            command.CommandText = "SELECT coalesce(max(rowid),0) FROM listening_events";
            var snapshot = (long)command.ExecuteScalar()!;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // Compare elapsed periods, avoiding a full preceding week against an unfinished current week.
            if (filter.FromUnix is null || filter.FromUnix < now + 1)
                filter = filter with { ToUnix = Math.Min(filter.ToUnix ?? now + 1, now + 1) };
            var where = HistoryWhere(command, filter, epoch, snapshot);
            var totals = ReadHistoryTotals(command, where);
            HistoryTotals? previous = null;
            if (filter.FromUnix is { } from && Math.Min(filter.ToUnix ?? now, now) is var end && end > from)
            {
                var priorWhere = HistoryWhere(command, filter with { FromUnix = Math.Max(0, from - (end - from)), ToUnix = from }, epoch, snapshot);
                previous = ReadHistoryTotals(command, priorWhere);
            }
            HistoryWhere(command, filter, epoch, snapshot);
            var days = new List<ListeningDay>(366);
            command.CommandText = "SELECT date(e.started,'unixepoch','localtime'),count(*),sum(e.seconds) FROM listening_events e" + where + " GROUP BY 1 ORDER BY 1 DESC LIMIT 366";
            using (var r = command.ExecuteReader())
                while (r.Read()) { cancellation.ThrowIfCancellationRequested(); days.Add(new(r.GetString(0), r.GetInt64(1), r.GetDouble(2))); }
            days.Reverse();
            var tracks = ReadHistoryRanks(command, where, "e.track_id", "e.title", "e.artist", cancellation);
            var artists = ReadHistoryRanks(command, where, "e.artist", "e.artist", "''", cancellation);
            var albums = ReadHistoryRanks(command, where, "e.album,e.artist", "e.album", "e.artist", cancellation);
            var formats = ReadHistoryRanks(command, where, "upper(e.codec)", "upper(e.codec)", "''", cancellation);
            var quality = ReadHistoryRanks(command, where, "e.bit_depth,e.sample_rate", "CASE WHEN e.bit_depth>0 THEN e.bit_depth||' bit · ' ELSE '' END||CASE WHEN e.sample_rate>0 THEN round(e.sample_rate/1000.0,1)||' kHz' ELSE '—' END", "''", cancellation);
            command.CommandText = """
                WITH ordered AS (SELECT e.id,e.started,e.updated,e.seconds,lag(e.updated) OVER(ORDER BY e.started,e.id) prior
                """ + " FROM listening_events e" + where + """
                ), tagged AS (SELECT *,sum(CASE WHEN prior IS NULL OR started-prior>1800 THEN 1 ELSE 0 END)
                  OVER(ORDER BY started,id ROWS UNBOUNDED PRECEDING) session FROM ordered),
                sessions AS (SELECT sum(seconds) seconds FROM tagged GROUP BY session)
                SELECT count(*),coalesce(avg(seconds),0),coalesce(max(seconds),0) FROM sessions
                """;
            long sessions; double average, longest;
            using (var r = command.ExecuteReader()) { r.Read(); sessions = r.GetInt64(0); average = r.GetDouble(1); longest = r.GetDouble(2); }
            command.CommandText = "WITH days AS(SELECT DISTINCT date(e.started,'unixepoch','localtime') day FROM listening_events e" + where + """
                ), islands AS(SELECT julianday(day)-row_number() OVER(ORDER BY day) streak FROM days),
                streaks AS(SELECT count(*) n FROM islands GROUP BY streak) SELECT coalesce(max(n),0) FROM streaks
                """;
            var streak = (long)command.ExecuteScalar()!;
            command.CommandText = "SELECT count(*) FROM(SELECT e.track_id FROM listening_events e" + where + " GROUP BY e.track_id HAVING count(*)>1)";
            var replayed = (long)command.ExecuteScalar()!;
            long newTracks = 0, returning = 0, rediscovered = 0;
            if (filter.FromUnix is { } beginning)
            {
                command.Parameters.AddWithValue("$beginning", beginning);
                command.CommandText = """
                    WITH selected AS(SELECT DISTINCT e.track_id FROM listening_events e
                    """ + where + """
                    ), prior AS(SELECT selected.track_id,max(old.started) last FROM selected LEFT JOIN listening_events old
                      ON old.track_id=selected.track_id AND old.history_epoch=$epoch AND old.started<$beginning GROUP BY selected.track_id)
                    SELECT coalesce(sum(last IS NULL),0),coalesce(sum(last IS NOT NULL),0),coalesce(sum(last<$beginning-2592000),0) FROM prior
                    """;
                using var r = command.ExecuteReader(); r.Read(); newTracks = r.GetInt64(0); returning = r.GetInt64(1); rediscovered = r.GetInt64(2);
            }
            transaction.Commit();
            return new HistoryReview(totals, previous, days, tracks, artists, albums, formats,
                new(sessions, average, longest, streak), new(newTracks, returning, replayed, rediscovered,
                    totals.Plays == 0 ? 0 : 1 - (double)totals.Tracks / totals.Plays), quality);
        }, false, cancellation);

    private static HistoryTotals ReadHistoryTotals(SqliteCommand command, string where)
    {
        command.CommandText = "SELECT count(*),coalesce(sum(e.counted=1 AND e.ended IS NOT NULL),0),coalesce(sum(e.seconds),0),count(DISTINCT e.track_id),count(DISTINCT nullif(e.artist,'')),count(DISTINCT date(e.started,'unixepoch','localtime')) FROM listening_events e" + where;
        using var r = command.ExecuteReader(); r.Read();
        return new(r.GetInt64(0), r.GetInt64(1), r.GetDouble(2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5));
    }
    private static IReadOnlyList<HistoryRank> ReadHistoryRanks(SqliteCommand command, string where, string group, string name, string subtitle, CancellationToken token)
    {
        command.CommandText = $"SELECT {name},{subtitle},count(*) plays,sum(e.seconds) seconds FROM listening_events e{where} GROUP BY {group} ORDER BY seconds DESC,plays DESC,{group} LIMIT 8";
        var rows = new List<HistoryRank>(8);
        using var r = command.ExecuteReader();
        while (r.Read()) { token.ThrowIfCancellationRequested(); rows.Add(new(r.GetString(0), r.GetString(1), r.GetInt64(2), r.GetDouble(3))); }
        return rows;
    }
}
