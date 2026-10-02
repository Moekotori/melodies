using Echo.Contracts.Library;
using Echo.Infrastructure.Search;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : ILibraryReview
{
    public Task<LibraryReviewPage> GetLibraryReviewAsync(bool hidden, int offset, CancellationToken cancellation)
    {
        if (offset is < 0 or > 1000000) throw new ArgumentOutOfRangeException(nameof(offset));
        return _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var command = connection.CreateCommand();
            command.CommandText = hidden ? """
                SELECT t.id,t.title,t.artist,t.path,COUNT(*) OVER() FROM tracks t
                JOIN folder_hidden_tracks h ON h.track_id=t.id ORDER BY t.path_key,t.id LIMIT 200 OFFSET $offset
                """ : """
                WITH ranked AS (
                  SELECT id,title,artist,path,path_key,
                    ROW_NUMBER() OVER(PARTITION BY echo_fold(title),echo_fold(artist),round(duration*2) ORDER BY path_key,id) AS ordinal
                  FROM tracks WHERE missing=0 AND duration>0 AND trim(title)<>'')
                SELECT id,title,artist,path,COUNT(*) OVER() FROM ranked WHERE ordinal>1
                ORDER BY path_key,id LIMIT 200 OFFSET $offset
                """;
            command.Parameters.AddWithValue("$offset", offset);
            using var reader = command.ExecuteReader();
            var tracks = new List<LibraryReviewTrack>(200); long total = 0;
            while (reader.Read())
            {
                cancellation.ThrowIfCancellationRequested(); total = reader.GetInt64(4);
                tracks.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
            return new LibraryReviewPage(tracks, total);
        }, true, cancellation);
    }
}
