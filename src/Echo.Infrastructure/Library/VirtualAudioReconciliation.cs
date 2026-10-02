using Echo.Contracts.Library;
using Echo.Infrastructure.Files;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;
// Prepared once per batch; compiling UPDATE triggers for each song dominates large-library imports.
internal sealed class VirtualAudioReconciliation : IDisposable
{
    private readonly SqliteCommand _whole, _segments, _cleanup;
    public VirtualAudioReconciliation(SqliteConnection connection, SqliteTransaction transaction)
    {
        _whole = Prepare("UPDATE tracks SET missing=1 WHERE path_key=$path AND missing=0");
        _segments = Prepare("UPDATE tracks SET missing=1 WHERE physical_path=$path AND source_kind='cue' AND missing=0");
        _cleanup = Prepare("DELETE FROM artist_tracks WHERE track_id IN(SELECT id FROM tracks WHERE physical_path=$path AND source_kind='cue' AND missing=1)");
        SqliteCommand Prepare(string sql)
        {
            var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = sql; command.Parameters.Add(new("$path", SqliteType.Text));
            command.Prepare(); return command;
        }
    }
    public void Apply(ScanEntry entry, ArtistIndexWriter artists, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var source = entry.File.Source;
        if (source?.Kind == "osu") return;
        if (source?.Kind == "cue")
        {
            var key = LibraryPath.Key(source.AudioPath);
            _whole.Parameters[0].Value = key;
            if (_whole.ExecuteNonQuery() > 0) artists.Update(key, cancellation);
        }
        else
        {
            _segments.Parameters[0].Value = entry.File.Path;
            _segments.ExecuteNonQuery();
            cancellation.ThrowIfCancellationRequested();
            _cleanup.Parameters[0].Value = entry.File.Path;
            _cleanup.ExecuteNonQuery();
        }
    }
    public void Dispose() { _whole.Dispose(); _segments.Dispose(); _cleanup.Dispose(); }
}
