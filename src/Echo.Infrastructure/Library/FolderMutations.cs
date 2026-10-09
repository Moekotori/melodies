using Echo.Contracts.Library;
using Echo.Infrastructure.Files;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task RemoveFolderRootAsync(string path, CancellationToken cancellation = default)
    {
        var prefix = LibraryPath.RootPrefix(path);
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT count(*) FROM scan_jobs WHERE state='Running'";
            if ((long)command.ExecuteScalar()! > 0) throw new InvalidOperationException("Wait for the active scan before removing a root.");
            command.Parameters.AddWithValue("$prefix", prefix); command.Parameters.AddWithValue("$key", LibraryPath.Key(Path.TrimEndingDirectorySeparator(LibraryPath.Canonical(path))));
            command.CommandText = "DELETE FROM library_roots WHERE prefix=$prefix"; command.ExecuteNonQuery();
            command.CommandText = "DELETE FROM folder_rules WHERE path_key=$key"; command.ExecuteNonQuery();
            command.CommandText = """
                UPDATE tracks SET missing=1 WHERE path_key >= $prefix AND path_key < substr($prefix,1,length($prefix)-1)||$upperSeparator
                  AND NOT EXISTS(SELECT 1 FROM library_roots r WHERE tracks.path_key>=r.prefix
                    AND tracks.path_key<substr(r.prefix,1,length(r.prefix)-1)||$upperSeparator);
                DELETE FROM artist_tracks WHERE track_id IN(SELECT id FROM tracks WHERE missing=1);
                UPDATE library_state SET revision=revision+1 WHERE id=1;
                """;
            command.Parameters.AddWithValue("$upperSeparator", ((char)(Path.DirectorySeparatorChar + 1)).ToString());
            cancellation.ThrowIfCancellationRequested(); command.ExecuteNonQuery(); transaction.Commit(); return true;
        }, true, cancellation);
    }

    public Task SetTracksHiddenAsync(IReadOnlyList<string> ids, bool hidden, CancellationToken cancellation = default)
    {
        if (ids.Count is < 1 or > 200 || ids.Any(id => id.Length is 0 or > 64)) throw new ArgumentException("Select 1–200 track IDs."); var owned = ids.Distinct().ToArray();
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
            using var artists = new ArtistIndexWriter(connection, transaction); command.Parameters.AddWithValue("$id", "");
            foreach (var id in owned)
            {
                cancellation.ThrowIfCancellationRequested(); command.Parameters[0].Value = id;
                if (hidden)
                {
                    command.CommandText = "INSERT OR IGNORE INTO folder_hidden_tracks SELECT id,missing FROM tracks WHERE id=$id; UPDATE tracks SET missing=1 WHERE id=$id; DELETE FROM artist_tracks WHERE track_id=$id;";
                    command.ExecuteNonQuery();
                }
                else
                {
                    command.CommandText = "UPDATE tracks SET missing=coalesce((SELECT was_missing FROM folder_hidden_tracks WHERE track_id=$id),missing) WHERE id=$id; DELETE FROM folder_hidden_tracks WHERE track_id=$id;";
                    command.ExecuteNonQuery(); command.CommandText = "SELECT path_key FROM tracks WHERE id=$id";
                    if (command.ExecuteScalar() is string key) artists.Update(key, cancellation);
                }
            }
            command.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1"; command.ExecuteNonQuery(); transaction.Commit(); return true;
        }, true, cancellation);
    }

    internal static void ApplyTrackUserState(SqliteConnection connection, SqliteTransaction transaction, string pathKey)
    {
        using var command = CreateTrackUserStateCommand(connection, transaction);
        command.Parameters[0].Value = pathKey;
        command.ExecuteNonQuery();
    }

    internal static SqliteCommand CreateTrackUserStateCommand(SqliteConnection connection, SqliteTransaction transaction)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.Add(new("$key", SqliteType.Text));
        command.CommandText = """
            UPDATE tracks SET
              title=u.title,sort_key=upper(u.title),artist=u.artist,album=u.album,album_artist=u.album_artist,
              track_no=u.track_no,disc_no=u.disc_no,year=u.year,genre=u.genre,
              album_key=echo_album_key(tracks.path,u.album,u.album_artist,coalesce(tracks.source_kind,''),coalesce(tracks.source_json,''))
            FROM track_user_tags u WHERE tracks.id=u.track_id AND tracks.path_key=$key;
            UPDATE tracks SET missing=1 WHERE path_key=$key AND id IN(SELECT track_id FROM folder_hidden_tracks);
            DELETE FROM artist_tracks WHERE track_id IN(SELECT id FROM tracks WHERE path_key=$key AND missing=1);
            DELETE FROM folder_resource_facts WHERE track_id IN(SELECT id FROM tracks WHERE path_key=$key);
            """;
        command.Prepare();
        return command;
    }
}
