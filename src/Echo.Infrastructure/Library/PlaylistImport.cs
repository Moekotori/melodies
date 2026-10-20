using System.Globalization;
using Echo.Contracts.Library;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // One writer transaction for playlist creation, matching, occurrences and the operation receipt.
    // Cancellation, invalid data or zero matches leaves no empty/partially imported playlist behind.
    public async Task<PlaylistImportResult> ImportPlaylistAsync(string operationId, string name, string description,
        IReadOnlyList<string> paths, int skipped, CancellationToken cancellation = default)
    {
        name = ValidatePlaylistName(name, description);
        if (paths.Count is < 1 or > 10_000 || skipped < 0) throw new ArgumentException("歌单没有可匹配的曲目，或条目数超过上限。");
        var keys = paths.Select(value => value.StartsWith("remote://", StringComparison.Ordinal)
            ? value : LibraryPath.Key(LibraryPath.Canonical(value))).ToArray();
        var id = Guid.NewGuid().ToString("N"); var imported = 0; var omitted = skipped;
        var receipt = await PlaylistWrite(operationId, $"import\0{name}\0{description}\0{skipped}\0" + string.Join('\0', keys), id, command =>
        {
            command.CommandText = "INSERT INTO playlists(id,name,description,updated_at) VALUES($id,$name,$description,unixepoch())";
            command.Parameters.AddWithValue("$id", id); command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$description", description); command.ExecuteNonQuery();
            command.Parameters.Clear();
            command.CommandText = "SELECT id FROM tracks WHERE path_key=$path AND missing=0 LIMIT 1";
            var path = command.Parameters.AddWithValue("$path", ""); command.Prepare();
            using var insert = command.Connection!.CreateCommand(); insert.Transaction = command.Transaction;
            insert.CommandText = "INSERT INTO playlist_items(id,playlist_id,track_id,position) VALUES($item,$id,$track,$position)";
            insert.Parameters.AddWithValue("$id", id);
            var item = insert.Parameters.AddWithValue("$item", ""); var track = insert.Parameters.AddWithValue("$track", "");
            var position = insert.Parameters.AddWithValue("$position", 0); insert.Prepare();
            foreach (var key in keys)
            {
                cancellation.ThrowIfCancellationRequested(); path.Value = key;
                if (command.ExecuteScalar() is not string trackId) { omitted++; continue; }
                item.Value = Guid.NewGuid().ToString("N"); track.Value = trackId; position.Value = ++imported; insert.ExecuteNonQuery();
            }
            if (imported == 0) throw new InvalidOperationException("歌单中没有已入库且可用的曲目。请先把对应音乐目录加入曲库。");
        }, cancellation, () => string.Join('|', id, imported.ToString(CultureInfo.InvariantCulture), omitted.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
        var fields = receipt.Split('|');
        return new(fields[0], name, int.Parse(fields[1], CultureInfo.InvariantCulture), int.Parse(fields[2], CultureInfo.InvariantCulture));
    }
}
