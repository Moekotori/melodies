using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IAlbumMetadata
{
    public Task<AlbumMetadataSnapshot> GetAlbumMetadataAsync(string albumId, CancellationToken cancellation = default)
    {
        ValidateAlbumIdentity(albumId);
        return _interactiveReader.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                SELECT stable_id,file_title,file_artist FROM album_identity
                WHERE (stable_id=$id OR group_key=$id) AND group_key IN(SELECT id FROM albums) LIMIT 1;
                """;
            command.Parameters.AddWithValue("$id", albumId);
            string id, fileTitle, fileArtist;
            using (var reader = command.ExecuteReader())
            {
                if (!reader.Read()) throw new InvalidOperationException("Album is no longer available.");
                id = reader.GetString(0); fileTitle = reader.GetString(1); fileArtist = reader.GetString(2);
            }
            command.CommandText = "SELECT field,source,value FROM album_metadata_values WHERE album_id=$id";
            command.Parameters[0].Value = id;
            string? onlineTitle = null, userTitle = null, onlineArtist = null, userArtist = null;
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    if (reader.GetInt32(0) == 0)
                    { if (reader.GetInt32(1) == 1) onlineTitle = reader.GetString(2); else userTitle = reader.GetString(2); }
                    else { if (reader.GetInt32(1) == 1) onlineArtist = reader.GetString(2); else userArtist = reader.GetString(2); }
                }
            transaction.Commit();
            static MetadataFieldValue Field(string file, string? online, string? user)
                => new(user ?? online ?? file, user is not null ? MetadataSource.User : online is not null ? MetadataSource.Online : MetadataSource.File,
                    file, online, user);
            return new AlbumMetadataSnapshot(id, Field(fileTitle, onlineTitle, userTitle), Field(fileArtist, onlineArtist, userArtist));
        }, true, cancellation);
    }

    public Task SetAlbumMetadataAsync(string albumId, AlbumMetadataField field, MetadataSource source,
        string? value, CancellationToken cancellation = default)
    {
        ValidateAlbumIdentity(albumId);
        if (!Enum.IsDefined(field) || source is not (MetadataSource.Online or MetadataSource.User)
            || value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 1024 || value.Contains('\0')))
            throw new ArgumentException("Invalid metadata field, source or bounded value.");
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT stable_id FROM album_identity WHERE (stable_id=$id OR group_key=$id) AND group_key IN(SELECT id FROM albums) LIMIT 1";
            command.Parameters.AddWithValue("$id", albumId);
            var id = command.ExecuteScalar() as string ?? throw new InvalidOperationException("Album is no longer available.");
            command.Parameters[0].Value = id;
            command.Parameters.AddWithValue("$field", (int)field); command.Parameters.AddWithValue("$source", (int)source);
            if (value is null) command.CommandText = "DELETE FROM album_metadata_values WHERE album_id=$id AND field=$field AND source=$source";
            else
            {
                command.Parameters.AddWithValue("$value", value.Trim());
                command.CommandText = """
                    INSERT INTO album_metadata_values(album_id,field,source,value) VALUES($id,$field,$source,$value)
                    ON CONFLICT(album_id,field,source) DO UPDATE SET value=excluded.value WHERE value IS NOT excluded.value;
                    """;
            }
            var changed = command.ExecuteNonQuery();
            if (changed != 0) { command.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1"; command.ExecuteNonQuery(); }
            cancellation.ThrowIfCancellationRequested(); transaction.Commit();
            return true;
        }, true, cancellation);
    }

    private static void ValidateAlbumIdentity(string albumId)
    {
        if (string.IsNullOrWhiteSpace(albumId) || albumId.Length > 1100) throw new ArgumentException("Invalid album identity.");
    }
}
