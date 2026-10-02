using System.Text.Json;
using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    public Task<FolderUiState> GetFolderUiStateAsync(CancellationToken cancellation = default) =>
        _interactiveReader.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT last_path,width,tree_compact,tracks_compact,expanded,selected FROM folder_ui_state WHERE id=1";
            using var reader = command.ExecuteReader(); cancellation.ThrowIfCancellationRequested(); if (!reader.Read()) return new FolderUiState();
            using var expanded = JsonDocument.Parse(reader.GetString(4)); using var selected = JsonDocument.Parse(reader.GetString(5));
            return new FolderUiState(reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetDouble(1), reader.GetBoolean(2), reader.GetBoolean(3),
                expanded.RootElement.EnumerateArray().Select(item => item.GetString()!).ToArray(), selected.RootElement.EnumerateArray().Select(item => item.GetString()!).ToArray());
        }, true, cancellation);

    public Task SaveFolderUiStateAsync(FolderUiState state, CancellationToken cancellation = default)
    {
        var expanded = Strings(state.Expanded, 128); var selected = Strings(state.SelectedRoots, 32);
        if (!double.IsFinite(state.Width) || state.Width is < 150 or > 500 || state.LastPath?.Length > 4096) throw new ArgumentException("Invalid folder view preferences.");
        return _writer.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO folder_ui_state VALUES(1,$path,$width,$tree,$tracks,$expanded,$selected) ON CONFLICT(id) DO UPDATE SET last_path=excluded.last_path,width=excluded.width,tree_compact=excluded.tree_compact,tracks_compact=excluded.tracks_compact,expanded=excluded.expanded,selected=excluded.selected";
            command.Parameters.AddWithValue("$path", (object?)state.LastPath ?? DBNull.Value); command.Parameters.AddWithValue("$width", state.Width);
            command.Parameters.AddWithValue("$tree", state.TreeCompact); command.Parameters.AddWithValue("$tracks", state.TracksCompact);
            command.Parameters.AddWithValue("$expanded", expanded); command.Parameters.AddWithValue("$selected", selected);
            cancellation.ThrowIfCancellationRequested(); command.ExecuteNonQuery(); return true;
        }, true, cancellation);
    }
    private static string Strings(IReadOnlyList<string>? items, int limit)
    {
        items ??= []; if (items.Count > limit || items.Any(item => item.Length > 4096)) throw new ArgumentException("View snapshot exceeds its budget.");
        using var buffer = new MemoryStream(); using (var json = new Utf8JsonWriter(buffer)) { json.WriteStartArray(); foreach (var item in items) json.WriteStringValue(item); json.WriteEndArray(); }
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
