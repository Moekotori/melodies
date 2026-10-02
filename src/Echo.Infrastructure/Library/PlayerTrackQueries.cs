using Echo.Contracts.Library;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IPlayerTrackLibrary
{
    public Task<TrackRow?> FindTrackByPathAsync(string path, CancellationToken cancellation)
    {
        var key = LibraryPath.Key(LibraryPath.Canonical(path));
        return _interactiveReader.QueueAsync<TrackRow?>(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id,path,title,artist,album,duration,codec,sample_rate,bit_depth,sort_key,
                       track_no,disc_no,favorite
                FROM tracks WHERE path_key=$path AND missing=0 LIMIT 1;
                """;
            command.Parameters.AddWithValue("$path", key);
            using var reader = command.ExecuteReader();
            return !reader.Read() ? null : new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetDouble(5), reader.GetString(6),
                reader.GetInt32(7), reader.GetInt32(8), reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetInt32(11), reader.GetBoolean(12));
        }, false, cancellation);
    }
}
