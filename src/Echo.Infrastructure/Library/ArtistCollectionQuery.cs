using System.Globalization;
using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// Artist-scoped SQL aggregates: no complete track collection crosses the read lane or IPC.
internal static class ArtistCollectionQuery
{
    private const string Lossless = "lower(t.codec) IN ('flac','alac','wav','wave','aif','aiff','ape','wv','wavpack','dsd','dsf','dff','tta','tak')";
    private const string Members = " FROM artist_tracks m JOIN tracks t ON t.id=m.track_id WHERE m.grouping=$group AND m.artist_id=$artist AND t.missing=0 ";
    internal static ArtistCollectionFacts Read(SqliteCommand command, CancellationToken cancellation)
    {
        command.CommandText = "SELECT coalesce(sum(" + Lossless + "),0),count(nullif(trim(t.codec),''))" + Members;
        int lossless, total;
        using (var reader = command.ExecuteReader()) { reader.Read(); lossless = reader.GetInt32(0); total = reader.GetInt32(1); }
        command.CommandText = "SELECT t.codec,t.bit_depth,t.sample_rate,t.bitrate" + Members + """
             AND length(trim(t.codec))>0 ORDER BY
             (lower(t.codec) IN ('dsd','dsf','dff')) DESC,
            """ + Lossless + " DESC,coalesce(t.bit_depth,0) DESC,coalesce(t.sample_rate,0) DESC,coalesce(t.bitrate,0) DESC,t.id LIMIT 1";
        string? spec = null;
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                var parts = new List<string>(2) { reader.GetString(0).Trim().ToUpperInvariant() };
                var bits = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                var rate = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                var bitrate = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                var rateText = (rate / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "kHz";
                if (bits > 0 && rate > 0) parts.Add($"{bits}bit/{rateText}");
                else if (rate > 0) parts.Add(rateText);
                else if (bitrate > 0) parts.Add($"{Math.Round(bitrate / 1000d)}kbps");
                spec = string.Join(' ', parts);
            }
        }
        command.CommandText = """
            WITH RECURSIVE genres(label,rest) AS (
              SELECT '',replace(replace(replace(t.genre,';',','),'/',','),'、',',')||','
            """ + Members + """
              AND length(trim(t.genre))>0
              UNION ALL SELECT trim(substr(rest,1,instr(rest,',')-1)),substr(rest,instr(rest,',')+1)
              FROM genres WHERE length(rest)>0)
            SELECT min(label) FROM genres WHERE length(label)>0
            GROUP BY lower(label) ORDER BY count(*) DESC,lower(label) LIMIT 3
            """;
        var genres = new List<string>(3);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) { cancellation.ThrowIfCancellationRequested(); genres.Add(reader.GetString(0)); }
        command.CommandText = """
            SELECT count(DISTINCT m.album_id) FROM artist_tracks m JOIN tracks t ON t.id=m.track_id
            WHERE m.grouping=1 AND m.artist_id=$artist AND t.missing=0
            """;
        var primary = checked((int)(long)command.ExecuteScalar()!);
        command.CommandText = "SELECT count(*)" + Members + """
             AND NOT EXISTS(SELECT 1 FROM artist_tracks p WHERE p.track_id=m.track_id AND p.grouping=1 AND p.artist_id=$artist)
            """;
        var appearances = checked((int)(long)command.ExecuteScalar()!);
        return new(genres, lossless, total, spec, primary, appearances);
    }
}
