using System.Text.Json;
using Echo.Contracts.Library;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IFolderWorkbench
{
    public Task<IReadOnlyList<FolderRule>> GetFolderRulesAsync(CancellationToken cancellation = default) =>
        _interactiveReader.QueueAsync<IReadOnlyList<FolderRule>>(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT path,pinned,position,combined,recursive,sort,descending,watch,hide_duplicates,ignore_json FROM folder_rules ORDER BY pinned DESC,position,path_key LIMIT 512";
            var rows = new List<FolderRule>(); using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested(); using var json = JsonDocument.Parse(reader.GetString(9));
                rows.Add(new(reader.GetString(0), reader.GetBoolean(1), reader.GetInt32(2), reader.GetBoolean(3),
                    reader.GetBoolean(4), (FolderTrackSort)reader.GetInt32(5), reader.GetBoolean(6), reader.GetBoolean(7),
                    reader.GetBoolean(8), json.RootElement.EnumerateArray().Select(item => item.GetString()!).ToArray(), LibraryPath.Key(reader.GetString(0))));
            }
            return rows;
        }, true, cancellation);

    public Task SaveFolderRuleAsync(FolderRule rule, CancellationToken cancellation = default)
    {
        var path = Path.TrimEndingDirectorySeparator(LibraryPath.Canonical(rule.Path)); var ignore = rule.Ignore ?? [];
        if (!Enum.IsDefined(rule.Sort) || Math.Abs((long)rule.Position) > 1_000_000 || ignore.Count > 32 || ignore.Any(pattern => pattern.Length is 0 or > 200))
            throw new ArgumentException("Invalid bounded folder rule.");
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream)) { json.WriteStartArray(); foreach (var pattern in ignore) json.WriteStringValue(pattern); json.WriteEndArray(); }
        var ignoreJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        return _writer.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO folder_rules(path_key,path,pinned,position,combined,recursive,sort,descending,watch,hide_duplicates,ignore_json)
                VALUES($key,$path,$pin,$position,$combined,$recursive,$sort,$desc,$watch,$duplicates,$ignore)
                ON CONFLICT(path_key) DO UPDATE SET path=excluded.path,pinned=excluded.pinned,position=excluded.position,
                  combined=excluded.combined,recursive=excluded.recursive,sort=excluded.sort,descending=excluded.descending,
                  watch=excluded.watch,hide_duplicates=excluded.hide_duplicates,ignore_json=excluded.ignore_json;
                """;
            command.Parameters.AddWithValue("$key", LibraryPath.Key(path)); command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$pin", rule.Pinned); command.Parameters.AddWithValue("$position", rule.Position);
            command.Parameters.AddWithValue("$combined", rule.IncludeInCombined); command.Parameters.AddWithValue("$recursive", rule.Recursive);
            command.Parameters.AddWithValue("$sort", (int)rule.Sort); command.Parameters.AddWithValue("$desc", rule.Descending);
            command.Parameters.AddWithValue("$watch", rule.Watch); command.Parameters.AddWithValue("$duplicates", rule.HideDuplicates);
            command.Parameters.AddWithValue("$ignore", ignoreJson); cancellation.ThrowIfCancellationRequested(); command.ExecuteNonQuery(); return true;
        }, true, cancellation);
    }
}
