namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task SetAlbumStateAsync(string albumId, bool favorite, int rating, CancellationToken cancellation)
    {
        if (albumId.Length > 1100 || rating is < 0 or > 5) throw new ArgumentException("Invalid album state.");
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO album_user_state(album_id,favorite,rating)
                  SELECT a.id,$favorite,$rating FROM albums a JOIN album_identity i ON i.group_key=a.id
                  WHERE i.stable_id=$id OR a.id=$id
                ON CONFLICT(album_id) DO UPDATE SET favorite=excluded.favorite,rating=excluded.rating;
                """;
            command.Parameters.AddWithValue("$id", albumId);
            command.Parameters.AddWithValue("$favorite", favorite);
            command.Parameters.AddWithValue("$rating", rating);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Album is no longer available.");
            command.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1";
            command.ExecuteNonQuery(); transaction.Commit();
            return true;
        }, true, cancellation);
    }

    public Task SetTrackFavoriteAsync(string trackId, bool favorite, CancellationToken cancellation)
    {
        if (trackId.Length > 64) throw new ArgumentException("Invalid track state.");
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE tracks SET favorite=$favorite WHERE id=$id AND missing=0";
            command.Parameters.AddWithValue("$id", trackId); command.Parameters.AddWithValue("$favorite", favorite);
            if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Track is no longer available.");
            command.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1";
            command.ExecuteNonQuery(); transaction.Commit();
            return true;
        }, true, cancellation);
    }
}
