using Echo.Contracts.Library;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Metadata;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // Indexed virtual-identity range; ignored CUE files are never opened to recover their old membership.
    public Task<IReadOnlyList<FileSnapshot>> ReadDefinitionTracksAsync(string definitionPath, string? afterPathKey,
        CancellationToken cancellation)
    {
        var lower = LibraryPath.Key(LibraryPath.Canonical(definitionPath) + "#cueTrack=");
        if (lower.Length > 4110 || afterPathKey?.Length > 4096) throw new ArgumentException("Invalid definition page.");
        var upper = lower[..^1] + (char)(lower[^1] + 1);
        return _scanReader.QueueAsync<IReadOnlyList<FileSnapshot>>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT path,path_key,size_bytes,modified_ticks,source_json FROM tracks
                WHERE path_key >= $lo AND path_key < $hi AND path_key > $after AND missing=0
                ORDER BY path_key LIMIT 64
                """;
            command.Parameters.AddWithValue("$lo", lower); command.Parameters.AddWithValue("$hi", upper);
            command.Parameters.AddWithValue("$after", afterPathKey ?? "");
            var files = new List<FileSnapshot>();
            var characters = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested();
                var path = reader.GetString(0); var key = reader.GetString(1);
                var json = reader.IsDBNull(4) ? null : reader.GetString(4);
                files.Add(new(path, key, reader.GetInt64(2), reader.GetInt64(3),
                    Source: TrackSourceJson.Decode(json), PreserveExisting: true));
                characters += path.Length + key.Length + (json?.Length ?? 0);
                if (characters >= 128 * 1024) break;
            }
            return files;
        }, false, cancellation);
    }
}
