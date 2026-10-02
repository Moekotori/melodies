using System.Security.Cryptography;
using System.Text;
using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<string> CreatePlaylistAsync(string operationId, string name, string description, CancellationToken cancellation = default)
    {
        name = ValidatePlaylistName(name, description); var id = Guid.NewGuid().ToString("N");
        return PlaylistWrite(operationId, $"create\0{name}\0{description}", id, command =>
        {
            command.CommandText = "INSERT INTO playlists(id,name,description,updated_at) VALUES($id,$name,$description,unixepoch())";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$description", description); command.ExecuteNonQuery();
        }, cancellation);
    }
    public Task UpdatePlaylistAsync(string operationId, string id, string name, string description, CancellationToken cancellation = default)
    {
        ValidatePlaylistId(id); name = ValidatePlaylistName(name, description);
        return PlaylistWrite(operationId, $"update\0{id}\0{name}\0{description}", id, command =>
        {
            command.CommandText = "UPDATE playlists SET name=$name,description=$description,updated_at=unixepoch() WHERE id=$id AND archived=0";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$name", name); command.Parameters.AddWithValue("$description", description);
            RequirePlaylistChange(command);
        }, cancellation);
    }
    public Task ArchivePlaylistAsync(string operationId, string id, bool archived, CancellationToken cancellation = default)
    {
        ValidatePlaylistId(id);
        return PlaylistWrite(operationId, $"archive\0{id}\0{archived}", id, command =>
        {
            command.CommandText = "UPDATE playlists SET archived=$archived,updated_at=unixepoch() WHERE id=$id";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$archived", archived); RequirePlaylistChange(command);
        }, cancellation);
    }
    public Task AddPlaylistTracksAsync(string operationId, string id, IReadOnlyList<string> trackIds, CancellationToken cancellation = default)
    {
        ValidatePlaylistId(id); var ids = PlaylistBatch(trackIds);
        return PlaylistWrite(operationId, "add\0" + id + "\0" + string.Join('\0', ids), id, command =>
        {
            TouchPlaylist(command, id);
            command.CommandText = "SELECT coalesce(max(position),0) FROM playlist_items WHERE playlist_id=$id";
            var position = (long)command.ExecuteScalar()!;
            command.CommandText = "INSERT INTO playlist_items(id,playlist_id,track_id,position) SELECT $item,$id,id,$position FROM tracks WHERE id=$track AND missing=0";
            var item = command.Parameters.AddWithValue("$item", ""); var track = command.Parameters.AddWithValue("$track", "");
            var order = command.Parameters.AddWithValue("$position", 0L); command.Prepare();
            foreach (var trackId in ids)
            {
                cancellation.ThrowIfCancellationRequested(); item.Value = Guid.NewGuid().ToString("N"); track.Value = trackId; order.Value = ++position;
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Track is no longer available. Refresh and retry.");
            }
        }, cancellation);
    }
    public Task RemovePlaylistItemsAsync(string operationId, string id, IReadOnlyList<string> itemIds, CancellationToken cancellation = default)
    {
        ValidatePlaylistId(id); var ids = PlaylistBatch(itemIds);
        return PlaylistWrite(operationId, "remove\0" + id + "\0" + string.Join('\0', ids), id, command =>
        {
            TouchPlaylist(command, id); command.CommandText = "DELETE FROM playlist_items WHERE playlist_id=$id AND id=$item";
            var item = command.Parameters.AddWithValue("$item", ""); command.Prepare();
            foreach (var itemId in ids) { cancellation.ThrowIfCancellationRequested(); item.Value = itemId; command.ExecuteNonQuery(); }
        }, cancellation);
    }
    public Task MovePlaylistItemAsync(string operationId, string id, string itemId, bool up, long revision, CancellationToken cancellation = default)
    {
        ValidatePlaylistId(id); ValidatePlaylistId(itemId);
        return PlaylistWrite(operationId, $"move\0{id}\0{itemId}\0{up}\0{revision}", id, command =>
        {
            PlaylistRevision(command, revision); TouchPlaylist(command, id);
            command.CommandText = "SELECT position FROM playlist_items WHERE playlist_id=$id AND id=$item";
            command.Parameters.AddWithValue("$item", itemId);
            var position = command.ExecuteScalar() as long? ?? throw new InvalidOperationException("Playlist item is no longer available.");
            command.Parameters.AddWithValue("$position", position);
            command.CommandText = "SELECT id,position FROM playlist_items WHERE playlist_id=$id AND position" + (up ? "<$position ORDER BY position DESC" : ">$position ORDER BY position") + " LIMIT 1";
            string neighbor; long other;
            using (var reader = command.ExecuteReader()) { if (!reader.Read()) return; neighbor = reader.GetString(0); other = reader.GetInt64(1); }
            command.CommandText = "UPDATE playlist_items SET position=CASE id WHEN $item THEN $other ELSE $position END WHERE playlist_id=$id AND id IN($item,$neighbor)";
            command.Parameters.AddWithValue("$other", other); command.Parameters.AddWithValue("$neighbor", neighbor); command.ExecuteNonQuery();
        }, cancellation);
    }
    private Task<string> PlaylistWrite(string operationId, string request, string result, Action<SqliteCommand> action, CancellationToken cancellation)
    {
        ValidatePlaylistId(operationId);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request)));
        return _writer.QueueAsync(connection =>
        {
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT fingerprint,result FROM playlist_receipts WHERE operation_id=$operation";
            command.Parameters.AddWithValue("$operation", operationId);
            using (var reader = command.ExecuteReader())
                if (reader.Read())
                {
                    if (reader.GetString(0) != fingerprint) throw new ArgumentException("Operation ID was reused for a different command.");
                    return reader.GetString(1);
                }
            command.Parameters.Clear(); action(command); cancellation.ThrowIfCancellationRequested();
            command.Parameters.Clear(); command.Parameters.AddWithValue("$operation", operationId);
            command.Parameters.AddWithValue("$fingerprint", fingerprint); command.Parameters.AddWithValue("$result", result);
            command.CommandText = """
                UPDATE library_state SET revision=revision+1 WHERE id=1;
                INSERT INTO playlist_receipts(operation_id,fingerprint,result) VALUES($operation,$fingerprint,$result);
                DELETE FROM playlist_receipts WHERE sequence < (SELECT coalesce(max(sequence),0)-255 FROM playlist_receipts);
                """;
            command.ExecuteNonQuery(); transaction.Commit(); return result;
        }, true, cancellation);
    }
    private static void TouchPlaylist(SqliteCommand command, string id)
    {
        command.Parameters.AddWithValue("$id", id);
        command.CommandText = "UPDATE playlists SET updated_at=unixepoch() WHERE id=$id AND archived=0"; RequirePlaylistChange(command);
    }
    private static void RequirePlaylistChange(SqliteCommand command)
    { if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Playlist is no longer available. Refresh and retry."); }
    private static string ValidatePlaylistName(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || description.Length > 1000 || name.Contains('\0') || description.Contains('\0'))
            throw new ArgumentException("Invalid playlist name or description.");
        return name.Trim();
    }
    private static string[] PlaylistBatch(IReadOnlyList<string> ids)
    {
        if (ids.Count is < 1 or > 200) throw new ArgumentException("Playlist batch must contain 1 to 200 items.");
        var copy = ids.ToArray(); foreach (var id in copy) ValidatePlaylistId(id); return copy;
    }
}
