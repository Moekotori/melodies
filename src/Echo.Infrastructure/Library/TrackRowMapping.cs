using Echo.Contracts.Library;
using Echo.Infrastructure.Metadata;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class TrackRowMapping
{
    public const string Columns = "id,path,title,artist,album,duration,codec,sample_rate,bit_depth,sort_key,track_no,disc_no,favorite,source_json";
    public const string AliasedColumns = "t.id,t.path,t.title,t.artist,t.album,t.duration,t.codec,t.sample_rate,t.bit_depth,t.sort_key,t.track_no,t.disc_no,t.favorite,t.source_json";
    public static TrackRow Read(SqliteDataReader reader, bool withGenre = false) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetDouble(5), reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8), reader.GetString(9),
        reader.IsDBNull(10) ? null : reader.GetInt32(10), reader.IsDBNull(11) ? null : reader.GetInt32(11),
        reader.GetBoolean(12), TrackSourceJson.Decode(reader.IsDBNull(13) ? null : reader.GetString(13)),
        withGenre && !reader.IsDBNull(14) ? reader.GetString(14) : "");
}
