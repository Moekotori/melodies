using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // Aggregates stay on the background read lane; DTO collections have fixed limits.
    public Task<ArtistInsights> GetArtistInsightsAsync(string id, ArtistGrouping grouping, CancellationToken cancellation = default)
    {
        ValidateArtist(id, grouping);
        return _scanReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, cancellation);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.Parameters.AddWithValue("$artist", id); command.Parameters.AddWithValue("$group", (int)grouping);
            command.CommandText = "SELECT revision FROM library_state WHERE id=1";
            var revision = (long)command.ExecuteScalar()!;
            command.CommandText = """
                SELECT coalesce(sum(t.favorite),0),min(CASE WHEN t.year BETWEEN 1000 AND 9999 THEN t.year END),max(CASE WHEN t.year BETWEEN 1000 AND 9999 THEN t.year END)
                FROM artist_tracks m JOIN tracks t ON t.id=m.track_id
                WHERE m.grouping=$group AND m.artist_id=$artist AND t.missing=0
                """;
            int favorites; int? firstYear, lastYear;
            using (var r = command.ExecuteReader())
            { r.Read(); favorites = r.GetInt32(0); firstYear = r.IsDBNull(1) ? null : r.GetInt32(1); lastYear = r.IsDBNull(2) ? null : r.GetInt32(2); }
            var history = """
                 FROM artist_tracks m JOIN listening_events e ON e.track_id=m.track_id
                 WHERE m.grouping=$group AND m.artist_id=$artist
                   AND e.history_epoch=(SELECT epoch FROM history_state WHERE id=1) AND e.counted=1 AND e.ended IS NOT NULL
                """;
            command.CommandText = "SELECT count(*),coalesce(sum(e.seconds),0),max(e.started)" + history;
            long plays; double seconds; long? last;
            using (var r = command.ExecuteReader())
            { r.Read(); plays = r.GetInt64(0); seconds = r.GetDouble(1); last = r.IsDBNull(2) ? null : r.GetInt64(2); }
            command.CommandText = """
                SELECT t.id,t.path,t.title,t.artist,t.album,t.duration,t.codec,t.sample_rate,t.bit_depth,t.sort_key,t.track_no,t.disc_no,t.favorite,t.source_json,
                  h.plays,h.seconds,h.last
                FROM (SELECT e.track_id,count(*) plays,sum(e.seconds) seconds,max(e.started) last
                """ + history + """
                 GROUP BY e.track_id ORDER BY plays DESC,last DESC,e.track_id LIMIT 5) h JOIN tracks t ON t.id=h.track_id
                 WHERE t.missing=0 ORDER BY h.plays DESC,h.last DESC,t.id
                """;
            var top = new List<ListeningTrack>(6);
            using (var r = command.ExecuteReader())
                while (r.Read()) { cancellation.ThrowIfCancellationRequested(); top.Add(new(TrackRowMapping.Read(r), r.GetInt64(14), r.GetDouble(15), r.GetInt64(16))); }
            command.CommandText = """
                WITH edges AS (
                  SELECT p.artist_id,0 grouping,count(*) shared,1 priority FROM artist_tracks m
                  JOIN artist_tracks p ON p.track_id=m.track_id AND p.grouping=0
                  JOIN tracks t ON t.id=m.track_id AND t.missing=0
                  WHERE m.grouping=$group AND m.artist_id=$artist AND p.artist_id<>$artist
                    AND EXISTS(SELECT 1 FROM artist_tracks own WHERE own.track_id=m.track_id AND own.grouping=0 AND own.artist_id=$artist)
                  GROUP BY p.artist_id
                  UNION ALL
                  SELECT p.artist_id,1,count(DISTINCT m.album_id),0 FROM artist_albums m
                  JOIN artist_tracks p ON p.album_id=m.album_id AND p.grouping=1
                  WHERE m.grouping=$group AND m.artist_id=$artist AND p.artist_id<>$artist
                  GROUP BY p.artist_id), chosen AS (
                  SELECT e.* FROM edges e WHERE e.priority=1 OR NOT EXISTS(
                    SELECT 1 FROM edges preferred WHERE preferred.artist_id=e.artist_id AND preferred.priority=1))
                SELECT a.id,a.name,a.track_count,a.album_count,a.duration,
                  (SELECT t.path FROM artist_tracks p JOIN tracks t ON t.id=p.track_id WHERE p.grouping=a.grouping AND p.artist_id=a.id AND t.missing=0 LIMIT 1),
                  r.shared,r.grouping,r.priority
                FROM chosen r JOIN artists a ON a.id=r.artist_id AND a.grouping=r.grouping
                ORDER BY r.priority DESC,r.shared DESC,a.sort_key,a.id LIMIT 8
                """;
            var related = new List<ArtistRelation>(8);
            using (var r = command.ExecuteReader())
                while (r.Read()) { cancellation.ThrowIfCancellationRequested(); related.Add(new(new(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetDouble(4), r.GetString(5), (ArtistGrouping)r.GetInt32(7)), r.GetInt32(6), r.GetInt32(8) == 1 ? "collaboration" : "sameAlbum")); }
            command.CommandText = """
                SELECT t.id,t.path,t.title,t.artist,t.album,t.duration,t.codec,t.sample_rate,t.bit_depth,t.sort_key,t.track_no,t.disc_no,t.favorite,t.source_json
                FROM artist_tracks m JOIN tracks t ON t.id=m.track_id WHERE m.grouping=$group AND m.artist_id=$artist AND t.missing=0
                  AND EXISTS(SELECT 1 FROM artist_tracks p WHERE p.track_id=m.track_id AND p.grouping=0 AND p.artist_id<>m.artist_id)
                  AND EXISTS(SELECT 1 FROM artist_tracks own WHERE own.track_id=m.track_id AND own.grouping=0 AND own.artist_id=$artist)
                ORDER BY t.sort_key,t.id LIMIT 8
                """;
            var collaborations = new List<TrackRow>(8);
            using (var r = command.ExecuteReader())
                while (r.Read()) { cancellation.ThrowIfCancellationRequested(); collaborations.Add(TrackRowMapping.Read(r)); }
            var unplayed = """
                 FROM artist_albums m JOIN albums a ON a.id=m.album_id JOIN album_identity i ON i.group_key=a.id
                 WHERE m.grouping=$group AND m.artist_id=$artist AND NOT EXISTS(
                   SELECT 1 FROM tracks t JOIN listening_events e ON e.track_id=t.id
                   WHERE t.album_key=a.id AND e.history_epoch=(SELECT epoch FROM history_state WHERE id=1) AND e.counted=1 AND e.ended IS NOT NULL)
                """;
            command.CommandText = "SELECT count(*)" + unplayed; var unplayedCount = checked((int)(long)command.ExecuteScalar()!);
            command.CommandText = """
                SELECT i.stable_id,i.display_title,i.display_artist,a.track_count,a.duration,a.year,
                  (SELECT path FROM tracks WHERE album_key=a.id AND missing=0 ORDER BY disc_no,track_no,id LIMIT 1),a.lossless_count
                """ + unplayed + " ORDER BY a.year DESC,a.id LIMIT 6";
            var albums = new List<AlbumRow>(6);
            using (var r = command.ExecuteReader())
                while (r.Read()) { cancellation.ThrowIfCancellationRequested(); albums.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetDouble(4), r.IsDBNull(5) ? null : r.GetInt32(5), r.GetString(6), r.GetInt32(7))); }
            var collection = ArtistCollectionQuery.Read(command, cancellation);
            var credits = new List<ArtistCollaborationCredit>(collaborations.Count);
            command.CommandText = "SELECT name FROM artist_tracks WHERE grouping=0 AND track_id=$creditTrack AND artist_id<>$artist ORDER BY artist_id LIMIT 8";
            var creditTrack = command.Parameters.Add("$creditTrack", SqliteType.Text);
            foreach (var track in collaborations)
            {
                creditTrack.Value = track.Id; var names = new List<string>(4);
                using var reader = command.ExecuteReader();
                while (reader.Read()) { cancellation.ThrowIfCancellationRequested(); names.Add(reader.GetString(0)); }
                credits.Add(new(track.Id, names));
            }
            transaction.Commit();
            return new ArtistInsights(revision, favorites, firstYear, lastYear, plays, seconds, last, unplayedCount, top, related, collaborations, albums, collection, credits);
        }, false, cancellation);
    }
}
