using Echo.Contracts.Library;
using Echo.Infrastructure.Search;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IHistoryLibrary
{
    private const string HistoryFrom = " FROM listening_events e LEFT JOIN tracks t ON t.id=e.track_id ";
    public Task<long> GetHistoryEpochAsync(CancellationToken cancellation = default)
        => _interactiveReader.QueueAsync(connection => ReadHistoryEpoch(connection), true, cancellation);
    private static long ReadHistoryEpoch(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT epoch FROM history_state WHERE id=1";
        return (long)command.ExecuteScalar()!;
    }
    private static string HistoryWhere(SqliteCommand command, HistoryFilter filter, long epoch, long snapshot)
    {
        if (filter.Search.Length > 128 || filter.FromUnix < 0 || filter.ToUnix < 0 || filter.FromUnix >= filter.ToUnix)
            throw new ArgumentException("Invalid history filter.");
        command.Parameters.Clear(); command.Parameters.AddWithValue("$epoch", epoch); command.Parameters.AddWithValue("$snapshot", snapshot);
        var where = " WHERE e.history_epoch=$epoch AND e.rowid<=$snapshot";
        if (filter.FromUnix is { } from) { command.Parameters.AddWithValue("$from", from); where += " AND e.started >= $from"; }
        if (filter.ToUnix is { } to) { command.Parameters.AddWithValue("$to", to); where += " AND e.started < $to"; }
        if (filter.CountedOnly) where += " AND e.counted=1 AND e.ended IS NOT NULL";
        var number = 0;
        foreach (var term in filter.Search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(12))
        {
            var name = "$term" + number++;
            var folded = LibrarySearchSchema.Fold(term);
            // LIKE can use the trigram index for >=3-character terms; shorter terms remain cancellable.
            // Escaped wildcards use instr on the snapshots, preserving literal search semantics.
            if (folded.IndexOfAny(['%', '_']) >= 0)
            {
                command.Parameters.AddWithValue(name, folded);
                where += $" AND (instr(echo_fold(e.title),{name})>0 OR instr(echo_fold(e.artist),{name})>0 OR instr(echo_fold(e.album),{name})>0)";
            }
            else
            {
                command.Parameters.AddWithValue(name, "%" + folded + "%");
                where += $" AND e.rowid IN(SELECT rowid FROM history_search WHERE title LIKE {name} OR artist LIKE {name} OR album LIKE {name})";
            }
        }
        return where;
    }
    public Task<HistoryPage> GetHistoryAsync(HistoryFilter filter, HistoryCursor? after = null, int limit = 200, CancellationToken cancellation = default)
    {
        if (limit is < 1 or > 200 || after is { Id.Length: > 64 }) throw new ArgumentException("Invalid history page.");
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            var epoch = ReadHistoryEpoch(connection, transaction);
            if (after is not null && (after.Epoch != epoch || after.QueryKey != filter.ToString())) throw new LibraryChangedException();
            command.CommandText = "SELECT coalesce(max(rowid),0) FROM listening_events";
            var snapshot = after?.Snapshot ?? (long)command.ExecuteScalar()!;
            var where = HistoryWhere(command, filter, epoch, snapshot);
            long? total = null;
            if (after is null) { command.CommandText = "SELECT count(*) FROM listening_events e" + where; total = (long)command.ExecuteScalar()!; }
            var pageWhere = where;
            if (after is not null)
            {
                command.Parameters.AddWithValue("$start", after.StartedUnix); command.Parameters.AddWithValue("$id", after.Id);
                pageWhere += after.Newer ? " AND (e.started,e.id)>($start,$id)" : " AND (e.started,e.id)<($start,$id)";
            }
            command.Parameters.AddWithValue("$limit", limit + 1);
            command.CommandText = """
                SELECT e.id,e.track_id,coalesce(t.path,e.path),e.title,e.artist,e.album,e.duration,e.codec,
                  t.source_json,e.started,e.updated,e.ended,e.seconds,e.counted,e.source,
                  coalesce(t.missing=0,0),e.sample_rate,e.bit_depth
                """ + HistoryFrom + pageWhere + (after?.Newer == true ? " ORDER BY e.started,e.id LIMIT $limit" : " ORDER BY e.started DESC,e.id DESC LIMIT $limit");
            var rows = new List<HistoryRecord>(limit + 1);
            using (var r = command.ExecuteReader())
                while (r.Read())
                {
                    cancellation.ThrowIfCancellationRequested();
                    var track = new TrackRow(r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5),
                        r.GetDouble(6), r.GetString(7), r.GetInt32(16), r.GetInt32(17), "", Source: Echo.Infrastructure.Metadata.TrackSourceJson.Decode(r.IsDBNull(8) ? null : r.GetString(8)));
                    rows.Add(new(r.GetString(0), track, r.GetInt64(9), r.GetInt64(10), r.IsDBNull(11) ? null : r.GetInt64(11),
                        r.GetDouble(12), r.GetBoolean(13), r.GetString(14), r.GetBoolean(15)));
                }
            HistoryCursor? next = null, previous = null;
            var hasExtra = rows.Count > limit;
            if (hasExtra) rows.RemoveAt(limit);
            if (after?.Newer == true) rows.Reverse();
            if (rows.Count > 0)
            {
                var last = rows[^1]; var first = rows[0];
                if (after?.Newer == true || hasExtra) next = new(last.StartedUnix, last.Id, snapshot, epoch, filter.ToString());
                if (after?.Newer == true ? hasExtra : after is not null) previous = new(first.StartedUnix, first.Id, snapshot, epoch, filter.ToString(), Newer: true);
            }
            var days = new List<HistoryDay>();
            HistoryWhere(command, filter, epoch, snapshot);
            var dayKeys = rows.Select(row => DateTimeOffset.FromUnixTimeSeconds(row.StartedUnix).ToLocalTime().ToString("yyyy-MM-dd")).Distinct().ToArray();
            // One indexed range and one aggregate for all visible days, including off-page plays on those days.
            if (dayKeys.Length > 0)
            {
                var first = DateTime.ParseExact(dayKeys.Min()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                var last = DateTime.ParseExact(dayKeys.Max()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture).AddDays(1);
                command.Parameters.AddWithValue("$dayStart", new DateTimeOffset(first).ToUnixTimeSeconds());
                command.Parameters.AddWithValue("$dayEnd", new DateTimeOffset(last).ToUnixTimeSeconds());
                command.CommandText = "SELECT date(e.started,'unixepoch','localtime'),count(*),count(DISTINCT e.track_id),coalesce(sum(e.seconds),0),coalesce(sum(e.ended IS NULL),0) FROM listening_events e" + where + " AND e.started >= $dayStart AND e.started < $dayEnd GROUP BY 1 ORDER BY 1 DESC LIMIT 200";
                using var r = command.ExecuteReader();
                while (r.Read()) { cancellation.ThrowIfCancellationRequested(); days.Add(new(r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.GetDouble(3), r.GetInt64(4))); }
            }
            transaction.Commit(); return new HistoryPage(rows, next, snapshot, epoch, days, total, previous);
        }, true, cancellation);
    }
}
