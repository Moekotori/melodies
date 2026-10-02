using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<TrackTagState> GetTrackTagsAsync(string id, CancellationToken cancellation = default) =>
        _interactiveReader.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT title,artist,album,album_artist,track_no,disc_no,year,genre,path,json_extract(source_json,'$.AudioPath'),source_json IS NOT NULL FROM tracks WHERE id=$id";
            command.Parameters.AddWithValue("$id", id); using var reader = command.ExecuteReader(); cancellation.ThrowIfCancellationRequested();
            if (!reader.Read()) throw new ArgumentException("Track no longer exists.");
            return new TrackTagState(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetString(7)),
                reader.GetString(8), reader.IsDBNull(9) ? null : reader.GetString(9), reader.GetBoolean(10));
        }, true, cancellation);

    public Task SaveTrackTagsAsync(string id, TrackTagEdit tags, CancellationToken cancellation = default)
    {
        if (id.Length is 0 or > 64 || string.IsNullOrWhiteSpace(tags.Title) || new[] { tags.Title, tags.Artist, tags.Album, tags.AlbumArtist, tags.Genre }.Any(value => value.Length > 1024)
            || tags.TrackNumber is < 0 or > 9999 || tags.DiscNumber is < 0 or > 9999 || tags.Year is < 1000 or > 2100) throw new ArgumentException("Invalid track tags.");
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.Parameters.AddWithValue("$id", id); command.CommandText = "SELECT path_key FROM tracks WHERE id=$id";
            var key = command.ExecuteScalar() as string ?? throw new ArgumentException("Track no longer exists.");
            command.CommandText = """
                INSERT INTO track_user_tags(track_id,title,artist,album,album_artist,track_no,disc_no,year,genre)
                VALUES($id,$title,$artist,$album,$albumArtist,$track,$disc,$year,$genre)
                ON CONFLICT(track_id) DO UPDATE SET title=excluded.title,artist=excluded.artist,album=excluded.album,
                  album_artist=excluded.album_artist,track_no=excluded.track_no,disc_no=excluded.disc_no,year=excluded.year,genre=excluded.genre;
                """;
            command.Parameters.AddWithValue("$title", tags.Title.Trim()); command.Parameters.AddWithValue("$artist", tags.Artist.Trim());
            command.Parameters.AddWithValue("$album", tags.Album.Trim()); command.Parameters.AddWithValue("$albumArtist", tags.AlbumArtist.Trim());
            command.Parameters.AddWithValue("$track", (object?)tags.TrackNumber ?? DBNull.Value); command.Parameters.AddWithValue("$disc", (object?)tags.DiscNumber ?? DBNull.Value);
            command.Parameters.AddWithValue("$year", (object?)tags.Year ?? DBNull.Value); command.Parameters.AddWithValue("$genre", tags.Genre.Trim());
            cancellation.ThrowIfCancellationRequested(); command.ExecuteNonQuery(); ApplyTrackUserState(connection, transaction, key);
            using var artists = new ArtistIndexWriter(connection, transaction); artists.Update(key, cancellation);
            command.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1"; command.ExecuteNonQuery(); transaction.Commit(); return true;
        }, true, cancellation);
    }

    public async Task RepairFolderIndexesAsync(CancellationToken cancellation = default)
    {
        string? after = null;
        while (true)
        {
            var last = after;
            var batch = await _writer.QueueAsync(connection =>
            {
                using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
                command.CommandText = "SELECT path_key FROM tracks WHERE missing=0" + (last is null ? "" : " AND path_key>$after") + " ORDER BY path_key LIMIT 200";
                if (last is not null) command.Parameters.AddWithValue("$after", last);
                var keys = new List<string>(200); using (var reader = command.ExecuteReader()) while (reader.Read()) { cancellation.ThrowIfCancellationRequested(); keys.Add(reader.GetString(0)); }
                using var artists = new ArtistIndexWriter(connection, transaction);
                foreach (var key in keys)
                {
                    cancellation.ThrowIfCancellationRequested(); command.Parameters.Clear(); command.Parameters.AddWithValue("$key", key);
                    command.CommandText = "UPDATE tracks SET missing=1 WHERE path_key=$key; UPDATE tracks SET missing=0 WHERE path_key=$key;"; command.ExecuteNonQuery();
                    artists.Update(key, cancellation);
                }
                command.Parameters.Clear(); command.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1"; command.ExecuteNonQuery();
                transaction.Commit(); return keys;
            }, false, cancellation).ConfigureAwait(false);
            if (batch.Count < 200) break; after = batch[^1];
        }
    }
}
