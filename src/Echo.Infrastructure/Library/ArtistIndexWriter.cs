using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Prepared once per scan batch or migration. All membership and aggregate changes share its transaction.
internal sealed class ArtistIndexWriter : IDisposable
{
    private readonly SqliteCommand _track, _delete, _insert, _membership, _peers, _dropGrouping, _album;
    private readonly ArtistCreditPolicy _policy;
    private readonly ArtistRoleWriter? _roles;
    public ArtistIndexWriter(SqliteConnection connection, SqliteTransaction transaction)
    {
        var hasRoles = ArtistCreditPolicy.Exists(connection, transaction);
        _policy = new(hasRoles ? ArtistCreditPolicy.Read(connection, transaction).Delimiters : [";"]);
        if (hasRoles) _roles = new(connection, transaction, _policy);
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
        _dropGrouping = Prepare("DELETE FROM artist_tracks WHERE track_id=$track AND grouping=$group", ["$track", "$group"]);
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
        _roles?.Update(id);
    }
    private void Align(string album, string currentId, string shared, CancellationToken cancellation)
    {
        var expected = Names(shared, 1);
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
                ReplaceGrouping(1, peer.Id, shared, album, peer.Duration, peer.Added, cancellation);
            }
            _peers.Parameters[1].Value = peers[^1].Id;
        }
    }
    private void Insert(int grouping, string source, string id, string album, double duration, long added, CancellationToken cancellation)
    {
        foreach (var name in Names(source, grouping, id))
        {
            cancellation.ThrowIfCancellationRequested();
            object[] values = [grouping, ArtistCredits.Key(name), name, id, album, duration, added];
            for (var i = 0; i < values.Length; i++) _insert.Parameters[i].Value = values[i];
            _insert.ExecuteNonQuery();
        }
    }
    internal void ReplaceGrouping(int grouping, string id, string source, string album, double duration, long added,
        CancellationToken cancellation = default)
    {
        _dropGrouping.Parameters[0].Value = id; _dropGrouping.Parameters[1].Value = grouping; _dropGrouping.ExecuteNonQuery();
        Insert(grouping, source, id, album, duration, added, cancellation);
        _roles?.Update(id);
    }
    private string[] Names(string source, int grouping, string? id = null)
    {
        // ECHO's album-artist view keeps the full credit, including collaborations.
        if (grouping == 1)
        {
            var display = ArtistCredits.Display(source);
            return [display.Length == 0 ? "未知艺术家" : display];
        }
        var names = id is not null && _roles is not null ? _roles.SongNames(id, source) : _policy.Split(source);
        return names.Length == 0 ? ["未知艺术家"] : names;
    }
    public void Dispose()
    {
        _track.Dispose(); _delete.Dispose(); _insert.Dispose();
        _membership.Dispose(); _peers.Dispose(); _dropGrouping.Dispose(); _album.Dispose();
        _roles?.Dispose();
    }
}
