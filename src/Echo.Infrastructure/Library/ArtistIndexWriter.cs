using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Prepared once per scan batch or migration. All membership and aggregate changes share its transaction.
internal sealed class ArtistIndexWriter : IDisposable
{
    private readonly SqliteCommand _track, _delete, _insert;
    public ArtistIndexWriter(SqliteConnection connection, SqliteTransaction transaction)
    {
        _track = Prepare("SELECT id,artist,album_credit,album_key,duration,updated_at FROM tracks WHERE path_key=$path AND missing=0", ["$path"]);
        _delete = Prepare("DELETE FROM artist_tracks WHERE track_id=$track", ["$track"]);
        _insert = Prepare("INSERT INTO artist_tracks(grouping,artist_id,name,track_id,album_id,duration,added) VALUES($group,$artist,$name,$track,$album,$duration,$added)",
            ["$group", "$artist", "$name", "$track", "$album", "$duration", "$added"]);
        SqliteCommand Prepare(string sql, string[] parameters)
        {
            var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.Add(new(parameter, SqliteType.Text));
            command.Prepare(); return command;
        }
    }
    public void Update(string pathKey, CancellationToken cancellation)
    {
        _track.Parameters[0].Value = pathKey;
        string id, credit, albumCredit, album;
        double duration; long added;
        using (var reader = _track.ExecuteReader())
        {
            if (!reader.Read()) return;
            id = reader.GetString(0); credit = reader.GetString(1); albumCredit = reader.GetString(2);
            album = reader.GetString(3); duration = reader.GetDouble(4); added = reader.GetInt64(5);
        }
        Replace(id, credit, albumCredit, album, duration, added, cancellation);
    }
    public void Replace(string id, string credit, string albumCredit, string album, double duration, long added, CancellationToken cancellation)
    {
        _delete.Parameters[0].Value = id; _delete.ExecuteNonQuery();
        for (var grouping = 0; grouping <= 1; grouping++)
        {
            var source = grouping == 0 ? credit : albumCredit;
            var names = ArtistCredits.Split(source).ToArray();
            if (names.Length == 0) names = ["未知艺术家"];
            foreach (var name in names)
            {
                cancellation.ThrowIfCancellationRequested();
                object[] values = [grouping, ArtistCredits.Key(name), name, id, album, duration, added];
                for (var i = 0; i < values.Length; i++) _insert.Parameters[i].Value = values[i];
                _insert.ExecuteNonQuery();
            }
        }
    }
    public void Dispose() { _track.Dispose(); _delete.Dispose(); _insert.Dispose(); }
}
