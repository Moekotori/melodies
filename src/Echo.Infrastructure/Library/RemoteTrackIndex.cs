using Echo.Contracts.Library;
using Echo.Contracts.Remote;
using Echo.Infrastructure.Metadata;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Remote rows share the library writer, album/genre triggers and artist index. They are not local files.
public sealed partial class LibraryDatabase : IRemoteTrackIndex
{
    public Task<RemoteIndexReceipt> WriteAsync(IReadOnlyList<RemoteIndexTrack> tracks, string syncToken, CancellationToken token)
    {
        if (tracks.Count is < 1 or > 64 || syncToken.Length is 0 or > 80) throw new ArgumentException("网盘索引批次无效。");
        var batch = tracks.ToArray();
        return _writer.QueueAsync(connection => WriteRemote(connection, batch, syncToken, token), false, token);
    }
    public Task<int> RetireUnseenAsync(string sourceId, string syncToken, CancellationToken token)
        => RetireAsync(sourceId, syncToken, false, token);
    public Task<int> RetireSourceAsync(string sourceId, CancellationToken token)
        => RetireAsync(sourceId, null, true, token);
    public Task<IReadOnlyList<string>> PresentAsync(string sourceId, IReadOnlyList<string> remotePaths, CancellationToken token)
    {
        if (sourceId.Length is 0 or > 80 || remotePaths.Count > 200) throw new ArgumentException("网盘索引查询无效。");
        var keys = new string[remotePaths.Count];
        var paths = remotePaths.ToArray();
        for (var i = 0; i < paths.Length; i++)
            keys[i] = RemoteTrackPaths.Compose(sourceId, paths[i].StartsWith('/') ? paths[i] : "/" + paths[i]);
        return _writer.QueueAsync(connection => Lookup(connection, keys, paths), true, token);
    }
    private static IReadOnlyList<string> Lookup(SqliteConnection connection, string[] keys, string[] paths)
    {
        if (keys.Length == 0) return [];
        using var command = connection.CreateCommand();
        var slots = new string[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            slots[i] = "$k" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            command.Parameters.AddWithValue(slots[i], keys[i]);
        }
        command.CommandText = "SELECT path_key FROM tracks WHERE source_kind='remote' AND missing=0 AND path_key IN (" + string.Join(',', slots) + ")";
        var found = new HashSet<string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read()) found.Add(reader.GetString(0));
        var present = new List<string>();
        for (var i = 0; i < keys.Length; i++) if (found.Contains(keys[i])) present.Add(paths[i]);
        return present;
    }
    private Task<int> RetireAsync(string sourceId, string? syncToken, bool entireSource, CancellationToken token)
    {
        if (sourceId.Length is 0 or > 80) throw new ArgumentException("网盘来源无效。");
        var prefix = RemoteTrackPaths.Prefix(sourceId);
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.Parameters.AddWithValue("$lo", prefix.Lower); command.Parameters.AddWithValue("$hi", prefix.Upper);
            command.CommandText = """
                UPDATE tracks SET missing=1 WHERE source_kind='remote' AND missing=0
                AND path_key>=$lo AND path_key<$hi
                """ + (entireSource ? "" : " AND dependency_stamp IS NOT $token");
            if (!entireSource) command.Parameters.AddWithValue("$token", syncToken);
            var missing = command.ExecuteNonQuery();
            if (entireSource)
            {
                EnsureAlbumCache(connection);
                using var cache = connection.CreateCommand(); cache.Transaction = transaction;
                cache.CommandText = "DELETE FROM remote_album_cache WHERE source_id=$source";
                cache.Parameters.AddWithValue("$source", sourceId); cache.ExecuteNonQuery();
            }
            if (missing > 0)
            {
                command.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1";
                command.ExecuteNonQuery();
            }
            token.ThrowIfCancellationRequested(); transaction.Commit(); return missing;
        }, false, token);
    }
    private static RemoteIndexReceipt WriteRemote(SqliteConnection connection, IReadOnlyList<RemoteIndexTrack> tracks, string syncToken, CancellationToken token)
    {
        using var transaction = connection.BeginTransaction();
        using var unchanged = connection.CreateCommand(); unchanged.Transaction = transaction;
        unchanged.CommandText = """
            UPDATE tracks SET dependency_stamp=$token
            WHERE path_key=$key AND size_bytes=$size AND modified_ticks=$mtime AND source_kind='remote' AND missing=0
            AND title=$title AND artist=$artist AND album=$album AND album_artist=$albumArtist AND duration=$duration
            AND genre=$genre AND codec=$codec AND ifnull(track_no,-1)=$track AND sample_rate=$sample AND bit_depth=$depth
            """;
        unchanged.Parameters.Add("$token", SqliteType.Text); unchanged.Parameters.Add("$key", SqliteType.Text);
        unchanged.Parameters.Add("$size", SqliteType.Integer); unchanged.Parameters.Add("$mtime", SqliteType.Integer);
        unchanged.Parameters.Add("$title", SqliteType.Text); unchanged.Parameters.Add("$artist", SqliteType.Text);
        unchanged.Parameters.Add("$album", SqliteType.Text); unchanged.Parameters.Add("$albumArtist", SqliteType.Text);
        unchanged.Parameters.Add("$duration", SqliteType.Real); unchanged.Parameters.Add("$genre", SqliteType.Text);
        unchanged.Parameters.Add("$codec", SqliteType.Text); unchanged.Parameters.Add("$track", SqliteType.Integer);
        unchanged.Parameters.Add("$sample", SqliteType.Integer); unchanged.Parameters.Add("$depth", SqliteType.Integer);
        using var existing = connection.CreateCommand(); existing.Transaction = transaction;
        existing.CommandText = "SELECT id FROM tracks WHERE path_key=$key"; existing.Parameters.Add("$key", SqliteType.Text);
        using var upsert = connection.CreateCommand(); upsert.Transaction = transaction;
        upsert.CommandText = """
            INSERT INTO tracks(id,path,path_key,size_bytes,modified_ticks,parser_version,title,sort_key,artist,
              album,album_artist,duration,codec,sample_rate,bit_depth,track_no,disc_no,year,genre,updated_at,
              source_json,dependency_stamp,physical_path,source_kind,album_key)
            VALUES($id,$path,$key,$size,$mtime,$version,$title,$sort,$artist,$album,$albumArtist,$duration,
              $codec,$sample,$depth,$track,$disc,$year,$genre,unixepoch(),$source,$stamp,$path,'remote',
              echo_album_key($path,$album,$albumArtist,'remote',coalesce($source,'')))
            ON CONFLICT(path_key) DO UPDATE SET
              path=excluded.path,size_bytes=excluded.size_bytes,modified_ticks=excluded.modified_ticks,
              parser_version=excluded.parser_version,title=excluded.title,sort_key=excluded.sort_key,
              artist=excluded.artist,album=excluded.album,album_artist=excluded.album_artist,
              duration=excluded.duration,codec=excluded.codec,sample_rate=excluded.sample_rate,bit_depth=excluded.bit_depth,
              track_no=excluded.track_no,disc_no=excluded.disc_no,year=excluded.year,genre=excluded.genre,missing=0,
              updated_at=excluded.updated_at,source_json=excluded.source_json,dependency_stamp=excluded.dependency_stamp,
              physical_path=excluded.physical_path,source_kind='remote',album_key=excluded.album_key
            WHERE tracks.path=excluded.path COLLATE BINARY
            """;
        foreach (var name in new[] { "$id", "$path", "$key", "$size", "$mtime", "$version", "$title", "$sort", "$artist", "$album", "$albumArtist", "$duration", "$codec", "$track", "$source", "$stamp", "$sample", "$depth", "$disc", "$year", "$genre" })
            upsert.Parameters.Add(new SqliteParameter(name, SqliteType.Text));
        using var artists = new ArtistIndexWriter(connection, transaction);
        using var userState = CreateTrackUserStateCommand(connection, transaction);
        var inserted = 0; var updated = 0; var same = 0;
        foreach (var track in tracks)
        {
            token.ThrowIfCancellationRequested();
            var stored = RemoteTrackPaths.Compose(track.SourceId, track.RemotePath);
            var title = track.Title.Length is > 0 and <= 1024 ? track.Title : Path.GetFileNameWithoutExtension(track.RemotePath);
            var albumArtist = track.AlbumArtist.Length == 0 ? track.Artist : track.AlbumArtist;
            var genre = track.Genre.Length > 128 ? track.Genre[..128] : track.Genre;
            if (title.Length is 0 or > 1024 || track.Artist.Length > 512 || track.Album.Length > 512 || albumArtist.Length > 512
                || track.Codec.Length is 0 or > 64 || genre.Length > 128 || track.SampleRate is < 0 or > 384000 || track.BitDepth is < 0 or > 32
                || track.SizeBytes < 0 || !double.IsFinite(track.DurationSeconds) || track.DurationSeconds < 0)
                throw new ArgumentException("网盘曲目资料无效。");
            unchanged.Parameters[0].Value = syncToken; unchanged.Parameters[1].Value = stored;
            unchanged.Parameters[2].Value = track.SizeBytes; unchanged.Parameters[3].Value = track.ModifiedTicks;
            unchanged.Parameters[4].Value = title; unchanged.Parameters[5].Value = track.Artist;
            unchanged.Parameters[6].Value = track.Album; unchanged.Parameters[7].Value = albumArtist;
            unchanged.Parameters[8].Value = track.DurationSeconds; unchanged.Parameters[9].Value = genre;
            unchanged.Parameters[10].Value = track.Codec; unchanged.Parameters[11].Value = track.TrackNumber ?? -1;
            unchanged.Parameters[12].Value = track.SampleRate; unchanged.Parameters[13].Value = track.BitDepth;
            if (unchanged.ExecuteNonQuery() > 0) { same++; continue; }
            existing.Parameters[0].Value = stored;
            var prior = existing.ExecuteScalar() as string;
            var id = prior ?? Guid.CreateVersion7().ToString("N");
            var source = TrackSourceJson.Encode(new TrackSource("remote", stored, track.RemotePath, syncToken, track.SizeBytes, track.ModifiedTicks, 0, 0,
                title, track.Artist, track.Album, albumArtist));
            if (source is null || source.Length > 16384) throw new ArgumentException("网盘曲目资料超过上限。");
            object?[] values = [id, stored, stored, track.SizeBytes, track.ModifiedTicks, TrackMetadata.ParserVersion, title, title.ToUpperInvariant(),
                track.Artist, track.Album, albumArtist, track.DurationSeconds, track.Codec,
                track.TrackNumber, source, syncToken, track.SampleRate, track.BitDepth, track.DiscNumber, track.Year, genre];
            for (var i = 0; i < values.Length; i++) upsert.Parameters[i].Value = values[i] ?? DBNull.Value;
            if (upsert.ExecuteNonQuery() == 0) throw new IOException("网盘曲目无法写入曲库。");
            userState.Parameters[0].Value = stored; userState.ExecuteNonQuery();
            artists.Update(stored, token);
            if (prior is null) inserted++; else updated++;
        }
        if (inserted + updated > 0)
        {
            using var revision = connection.CreateCommand(); revision.Transaction = transaction;
            revision.CommandText = "UPDATE library_state SET revision=revision+1 WHERE id=1"; revision.ExecuteNonQuery();
        }
        token.ThrowIfCancellationRequested(); transaction.Commit();
        return new(inserted, updated, same);
    }
    public Task<string?> ReadAlbumCacheAsync(string sourceId, string albumId, string fingerprint, CancellationToken token)
    {
        CacheKey(sourceId, albumId, fingerprint);
        var minimum = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7 * 24 * 3600;
        return _writer.QueueAsync(connection =>
        {
            EnsureAlbumCache(connection);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT songs_json FROM remote_album_cache WHERE source_id=$source AND album_id=$album AND fingerprint=$fingerprint AND verified_unix>$minimum";
            command.Parameters.AddWithValue("$source", sourceId); command.Parameters.AddWithValue("$album", albumId);
            command.Parameters.AddWithValue("$fingerprint", fingerprint); command.Parameters.AddWithValue("$minimum", minimum);
            return command.ExecuteScalar() as string;
        }, true, token);
    }
    public Task WriteAlbumCacheAsync(string sourceId, string albumId, string fingerprint, string payload, CancellationToken token)
    {
        CacheKey(sourceId, albumId, fingerprint);
        if (payload.Length is 0 or > 262144) return Task.CompletedTask;
        var verified = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return _writer.QueueAsync(connection =>
        {
            EnsureAlbumCache(connection);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO remote_album_cache(source_id,album_id,fingerprint,verified_unix,songs_json)
                VALUES($source,$album,$fingerprint,$verified,$songs)
                ON CONFLICT(source_id,album_id) DO UPDATE SET fingerprint=excluded.fingerprint, verified_unix=excluded.verified_unix, songs_json=excluded.songs_json
                """;
            command.Parameters.AddWithValue("$source", sourceId); command.Parameters.AddWithValue("$album", albumId);
            command.Parameters.AddWithValue("$fingerprint", fingerprint); command.Parameters.AddWithValue("$verified", verified);
            command.Parameters.AddWithValue("$songs", payload);
            command.ExecuteNonQuery(); return 0;
        }, false, token);
    }
    private static void CacheKey(string sourceId, string albumId, string fingerprint)
    {
        if (sourceId.Length is 0 or > 80 || albumId.Length is 0 or > 256 || fingerprint.Length is 0 or > 128)
            throw new ArgumentException("Subsonic 专辑缓存无效。");
    }
    private static void EnsureAlbumCache(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS remote_album_cache(
              source_id TEXT NOT NULL, album_id TEXT NOT NULL, fingerprint TEXT NOT NULL,
              verified_unix INTEGER NOT NULL, songs_json TEXT NOT NULL, PRIMARY KEY(source_id, album_id))
            """;
        command.ExecuteNonQuery();
    }
}
