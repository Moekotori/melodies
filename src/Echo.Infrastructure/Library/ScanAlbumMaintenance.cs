using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Owned by one SQLite lane. Only a scan transaction defers derived album display maintenance.
// Dirty identities live in that same transaction on disk, never in a whole-library managed set.
internal sealed class ScanAlbumMaintenance
{
    private bool _deferred;
    public void Register(SqliteConnection connection) => connection.CreateFunction("echo_scan_deferred", () => _deferred);
    public IDisposable Defer()
    {
        if (_deferred) throw new InvalidOperationException("Nested scan metadata transaction.");
        _deferred = true;
        return new Scope(this);
    }

    public static void Refresh(SqliteConnection connection, SqliteTransaction transaction, ArtistIndexWriter artists,
        IReadOnlyList<string> paths, CancellationToken cancellation)
    {
        using var interrupt = new ReadCancellation(connection, cancellation);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        cancellation.ThrowIfCancellationRequested();
        command.CommandText = AlbumDisplay.Refresh("id IN(SELECT album_id FROM scan_album_dirty)");
        command.ExecuteNonQuery();
        foreach (var path in paths) artists.Update(path, cancellation, align: false);
        command.CommandText = "SELECT album_id FROM scan_album_dirty WHERE album_id>$after ORDER BY album_id LIMIT 128";
        command.Parameters.AddWithValue("$after", "");
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var albums = new List<string>(128);
            using (var reader = command.ExecuteReader()) while (reader.Read()) albums.Add(reader.GetString(0));
            if (albums.Count == 0) break;
            foreach (var album in albums) artists.AlignAlbum(album, cancellation);
            command.Parameters[0].Value = albums[^1];
        }
        command.CommandText = "DELETE FROM scan_album_dirty"; command.ExecuteNonQuery();
    }

    private sealed class Scope(ScanAlbumMaintenance owner) : IDisposable
    {
        public void Dispose() => owner._deferred = false;
    }
}
