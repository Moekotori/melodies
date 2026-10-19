using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Prepared once per scan batch or migration. All membership and aggregate changes share its transaction.
internal sealed class ArtistIndexWriter : IDisposable
{
    private readonly SqliteCommand _track, _delete, _insert, _membership, _peers, _dropAlbumArtist, _album;
    public ArtistIndexWriter(SqliteConnection connection, SqliteTransaction transaction)
    {
        _track = Prepare("""
            SELECT t.id,t.artist,t.album_artist,coalesce(a.artist,t.album_credit),t.album_key,t.duration,t.updated_at
            FROM tracks t LEFT JOIN albums a ON a.id=t.album_key WHERE t.path_key=$path AND t.missing=0
            """, ["$path"]);
        _delete = Prepare("DELETE FROM artist_tracks WHERE track_id=$track", ["$track"]);
        _insert = Prepare("INSERT INTO artist_tracks(grouping,artist_id,name,track_id,album_id,duration,added) VALUES($group,$artist,$name,$track,$album,$duration,$added)",
            ["$group", "$artist", "$name", "$track", "$album", "$duration", "$added"]);
        _membership = Prepare("""
            SELECT DISTINCT a.artist_id FROM artist_tracks a
            WHERE a.grouping=1 AND a.album_id=$album AND EXISTS(
              SELECT 1 FROM tracks t WHERE t.id=a.track_id AND length(trim(t.album_artist))=0) LIMIT 65
            """, ["$album"]);
        _peers = Prepare("""
            SELECT id,duration,updated_at FROM tracks WHERE album_key=$album AND missing=0
              AND length(trim(album_artist))=0 AND id>$after AND id!=$id ORDER BY id LIMIT 128
            """, ["$album", "$after", "$id"]);
        _dropAlbumArtist = Prepare("DELETE FROM artist_tracks WHERE track_id=$track AND grouping=1", ["$track"]);
        _album = Prepare("SELECT artist FROM albums WHERE id=$album", ["$album"]);
        SqliteCommand Prepare(string sql, string[] parameters)
        {
            var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.Add(new(parameter, SqliteType.Text));
            command.Prepare(); return command;
        }
    }
    public void Update(string pathKey, CancellationToken cancellation, bool align = true)
    {
        _track.Parameters[0].Value = pathKey;
        string id, credit, rawAlbumArtist, albumLevel, album;
        double duration; long added;
        using (var reader = _track.ExecuteReader())
        {
            if (!reader.Read()) return;
            id = reader.GetString(0); credit = reader.GetString(1); rawAlbumArtist = reader.GetString(2);
            albumLevel = reader.GetString(3); album = reader.GetString(4); duration = reader.GetDouble(5); added = reader.GetInt64(6);
        }
        var shared = string.IsNullOrWhiteSpace(rawAlbumArtist) ? albumLevel : rawAlbumArtist;
        Replace(id, credit, shared, album, duration, added, cancellation);
        if (align && string.IsNullOrWhiteSpace(rawAlbumArtist)) Align(album, id, shared, cancellation);
    }
    public void AlignAlbum(string album, CancellationToken cancellation)
    {
        _album.Parameters[0].Value = album;
        if (_album.ExecuteScalar() is string shared) Align(album, "", shared, cancellation);
    }
    public void Replace(string id, string credit, string albumCredit, string album, double duration, long added, CancellationToken cancellation)
    {
        _delete.Parameters[0].Value = id; _delete.ExecuteNonQuery();
        for (var grouping = 0; grouping <= 1; grouping++)
            Insert(grouping, grouping == 0 ? credit : albumCredit, id, album, duration, added, cancellation);
    }
    private void Align(string album, string currentId, string shared, CancellationToken cancellation)
    {
        var expected = Names(shared);
        _membership.Parameters[0].Value = album;
        var actual = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = _membership.ExecuteReader())
            while (reader.Read()) actual.Add(reader.GetString(0));
        if (actual.Count == expected.Length && expected.All(name => actual.Contains(ArtistCredits.Key(name)))) return;
        _peers.Parameters[0].Value = album; _peers.Parameters[1].Value = ""; _peers.Parameters[2].Value = currentId;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var peers = new List<(string Id, double Duration, long Added)>(128);
            using (var reader = _peers.ExecuteReader())
                while (reader.Read()) peers.Add((reader.GetString(0), reader.GetDouble(1), reader.GetInt64(2)));
            if (peers.Count == 0) break;
            foreach (var peer in peers)
            {
                cancellation.ThrowIfCancellationRequested();
                _dropAlbumArtist.Parameters[0].Value = peer.Id; _dropAlbumArtist.ExecuteNonQuery();
                Insert(1, shared, peer.Id, album, peer.Duration, peer.Added, cancellation);
            }
            _peers.Parameters[1].Value = peers[^1].Id;
        }
    }
    private void Insert(int grouping, string source, string id, string album, double duration, long added, CancellationToken cancellation)
    {
        foreach (var name in Names(source))
        {
            cancellation.ThrowIfCancellationRequested();
            object[] values = [grouping, ArtistCredits.Key(name), name, id, album, duration, added];
            for (var i = 0; i < values.Length; i++) _insert.Parameters[i].Value = values[i];
            _insert.ExecuteNonQuery();
        }
    }
    private static string[] Names(string source)
    {
        var names = ArtistCredits.Split(source).ToArray();
        return names.Length == 0 ? ["未知艺术家"] : names;
    }
    public void Dispose()
    {
        _track.Dispose(); _delete.Dispose(); _insert.Dispose();
        _membership.Dispose(); _peers.Dispose(); _dropAlbumArtist.Dispose(); _album.Dispose();
    }
}
