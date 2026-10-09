using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Stored album keys replace the generated title+credit key. CUE track artists no longer split a sheet.
internal static class AlbumGroupingSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TEMP TABLE track_album_keys(track_id TEXT PRIMARY KEY, old_key TEXT NOT NULL, new_key TEXT NOT NULL);
            INSERT INTO track_album_keys
              SELECT id,album_key,echo_album_key(path,album,album_artist,coalesce(source_kind,''),coalesce(source_json,'')) FROM tracks;
            CREATE TEMP TABLE keep_album_state AS SELECT album_id,favorite,rating FROM album_user_state;
            CREATE TEMP TABLE keep_album_identity AS
              SELECT group_key,stable_id,display_title,display_artist FROM album_identity;
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT type,name,sql FROM sqlite_master WHERE type IN ('index','trigger') AND sql LIKE '%album_key%'";
        var dependents = new List<(string Type, string Name, string Sql)>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) dependents.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        foreach (var item in dependents)
        {
            command.CommandText = item.Type == "index"
                ? "DROP INDEX " + Quote(item.Name)
                : "DROP TRIGGER " + Quote(item.Name);
            command.ExecuteNonQuery();
        }
        command.CommandText = """
            ALTER TABLE tracks DROP COLUMN album_key;
            ALTER TABLE tracks ADD COLUMN album_key TEXT NOT NULL DEFAULT '';
            UPDATE tracks SET album_key=(SELECT new_key FROM track_album_keys WHERE track_id=tracks.id);
            """;
        command.ExecuteNonQuery();
        foreach (var item in dependents)
        {
            command.CommandText = item.Sql;
            command.ExecuteNonQuery();
        }
        command.CommandText = """
            DROP TRIGGER IF EXISTS tracks_album_insert;
            DROP TRIGGER IF EXISTS tracks_album_delete;
            DROP TRIGGER IF EXISTS tracks_album_update;
            """;
        command.ExecuteNonQuery();
        command.CommandText = AlbumSchema.TrackTriggers;
        command.ExecuteNonQuery();
        command.CommandText = """
            DELETE FROM albums;
            INSERT INTO albums(id,title,artist,sort_key,artist_key,track_count,duration,lossless_count,year,added)
              SELECT album_key,coalesce(nullif(trim(album),''),'未知专辑'),
                coalesce(nullif(album_credit,''),'未知艺术家'),
                upper(coalesce(nullif(trim(album),''),'未知专辑')),
                upper(coalesce(nullif(album_credit,''),'未知艺术家')),
                count(*),sum(duration),sum(lower(codec) IN ('flac','alac','wav','aiff','ape','wv')),
                max(year),max(updated_at)
              FROM tracks WHERE missing=0 GROUP BY album_key;
            """ + AlbumDisplay.Refresh("id IN(SELECT album_key FROM tracks WHERE missing=0)") + """
            DELETE FROM album_identity WHERE group_key NOT IN(SELECT id FROM albums);
            CREATE TEMP TABLE inherited_album AS
              SELECT new_id,stable_id,display_title,display_artist FROM (
                SELECT moved.new_id,identity.stable_id,identity.display_title,identity.display_artist,
                  row_number() OVER (PARTITION BY moved.new_id ORDER BY moved.tracks DESC,identity.stable_id) AS chosen
                FROM (
                  SELECT old_key,new_key AS new_id,count(*) AS tracks,
                    row_number() OVER (PARTITION BY old_key ORDER BY count(*) DESC,new_key) AS fragment
                  FROM track_album_keys GROUP BY old_key,new_key
                ) moved
                JOIN keep_album_identity identity ON identity.group_key=moved.old_key
                WHERE moved.fragment=1
              ) WHERE chosen=1;
            UPDATE album_identity SET stable_id=inherited.stable_id,display_title=inherited.display_title,
              display_artist=inherited.display_artist,title_key=upper(inherited.display_title),
              artist_key=upper(inherited.display_artist)
              FROM inherited_album inherited WHERE album_identity.group_key=inherited.new_id;
            UPDATE album_identity SET
              display_title=coalesce((SELECT value FROM album_metadata_values WHERE album_id=stable_id AND field=0 ORDER BY source DESC LIMIT 1),file_title),
              display_artist=coalesce((SELECT value FROM album_metadata_values WHERE album_id=stable_id AND field=1 ORDER BY source DESC LIMIT 1),file_artist);
            UPDATE album_identity SET title_key=upper(display_title),artist_key=upper(display_artist);
            DELETE FROM album_user_state;
            INSERT INTO album_user_state(album_id,favorite,rating)
              SELECT keys.new_key,max(state.favorite),max(state.rating)
              FROM track_album_keys keys JOIN keep_album_state state ON state.album_id=keys.old_key
              WHERE keys.new_key IN(SELECT id FROM albums) GROUP BY keys.new_key;
            UPDATE artist_tracks SET album_id=(SELECT album_key FROM tracks WHERE tracks.id=artist_tracks.track_id)
              WHERE EXISTS(SELECT 1 FROM tracks WHERE tracks.id=artist_tracks.track_id);
            DELETE FROM artist_albums;
            INSERT INTO artist_albums(grouping,artist_id,album_id,track_count)
              SELECT grouping,artist_id,album_id,count(*) FROM artist_tracks GROUP BY grouping,artist_id,album_id;
            UPDATE artists SET album_count=(SELECT count(*) FROM artist_albums
              WHERE grouping=artists.grouping AND artist_id=artists.id);
            UPDATE genre_tracks SET album_id=(SELECT album_key FROM tracks WHERE tracks.id=genre_tracks.track_id)
              WHERE EXISTS(SELECT 1 FROM tracks WHERE tracks.id=genre_tracks.track_id);
            DELETE FROM genre_albums;
            INSERT INTO genre_albums(genre_id,album_id,track_count)
              SELECT genre_id,album_id,count(*) FROM genre_tracks GROUP BY genre_id,album_id;
            UPDATE genres SET album_count=(SELECT count(*) FROM genre_albums WHERE genre_id=genres.id);
            DROP TABLE track_album_keys; DROP TABLE keep_album_state; DROP TABLE keep_album_identity; DROP TABLE inherited_album;
            CREATE TRIGGER IF NOT EXISTS tracks_album_key_guard BEFORE INSERT ON tracks
              WHEN length(new.album_key)<>40 BEGIN SELECT RAISE(ABORT,'album_key'); END;
            CREATE TRIGGER IF NOT EXISTS tracks_album_key_guard_update BEFORE UPDATE OF album_key ON tracks
              WHEN length(new.album_key)<>40 BEGIN SELECT RAISE(ABORT,'album_key'); END;
            PRAGMA user_version=21;
            """;
        command.ExecuteNonQuery();
        RepairSharedArtists(connection, transaction);
        command.CommandText = """
            DELETE FROM artist_albums;
            INSERT INTO artist_albums(grouping,artist_id,album_id,track_count)
              SELECT grouping,artist_id,album_id,count(*) FROM artist_tracks GROUP BY grouping,artist_id,album_id;
            UPDATE artists SET album_count=(SELECT count(*) FROM artist_albums
              WHERE grouping=artists.grouping AND artist_id=artists.id);
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    // Compilations keep one album. Their album-artist membership follows that shared name.
    private static void RepairSharedArtists(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var list = connection.CreateCommand();
        list.Transaction = transaction;
        list.CommandText = """
            SELECT min(t.path_key) FROM tracks t JOIN albums a ON a.id=t.album_key
            WHERE t.missing=0 AND length(trim(t.album_artist))=0
            GROUP BY t.album_key
            HAVING count(DISTINCT lower(trim(t.artist)))>1
            """;
        var paths = new List<string>();
        using (var reader = list.ExecuteReader())
            while (reader.Read()) paths.Add(reader.GetString(0));
        using var artists = new ArtistIndexWriter(connection, transaction);
        foreach (var path in paths) artists.Update(path, CancellationToken.None);
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
