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
                SELECT id,path,title,artist,coalesce((SELECT display_title FROM album_identity WHERE group_key=tracks.album_key),album),duration,codec,sample_rate,bit_depth,sort_key,
                       track_no,disc_no,favorite,source_json
                FROM tracks WHERE path_key=$path AND missing=0 LIMIT 1;
                """;
            command.Parameters.AddWithValue("$path", key);
            using var reader = command.ExecuteReader();
            return !reader.Read() ? null : TrackRowMapping.Read(reader);
        }, false, cancellation);
    }
}
