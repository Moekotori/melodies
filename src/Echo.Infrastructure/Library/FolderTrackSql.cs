using Echo.Contracts.Library;
using Echo.Infrastructure.Files;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class FolderTrackSql
{
    public const string Lossless = "upper(trim(t.codec)) IN ('FLAC','ALAC','WAV','WAVE','AIFF','AIF','AIFC','APE','WV','TTA','DSF','DFF','DSD','PCM')";
    public const string HiRes = "(t.sample_rate>48000 OR t.bit_depth>16)";
    public const string Plays = "(SELECT count(*) FROM listening_events e WHERE e.track_id=t.id AND e.counted=1 AND e.ended IS NOT NULL AND e.history_epoch=(SELECT epoch FROM history_state WHERE id=1))";
    public const string LastPlayed = "coalesce((SELECT max(e.started) FROM listening_events e WHERE e.track_id=t.id AND e.history_epoch=(SELECT epoch FROM history_state WHERE id=1)),0)";
    private const string Bpm = "coalesce(json_extract(t.source_json,'$.Bpm'),0)";

    public static string Scope(SqliteCommand command, FolderTrackQuery query, string alias = "t", string suffix = "")
    {
        var paths = query.Paths is { Count: > 0 } ? query.Paths : [query.Path];
        if (paths.Count is < 1 or > 32) throw new ArgumentException("Select 1–32 folders.");
        var conditions = new List<string>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            var prefix = LibraryPath.RootPrefix(paths[i]); var name = "$folder" + suffix + i;
            command.Parameters.AddWithValue(name + "Lo", prefix);
            command.Parameters.AddWithValue(name + "Hi", prefix[..^1] + (char)(prefix[^1] + 1));
            var condition = $"({alias}.path_key>={name}Lo AND {alias}.path_key<{name}Hi";
            if (!query.Recursive)
            {
                command.Parameters.AddWithValue(name + "Start", prefix.Length + 1);
                command.Parameters.AddWithValue(name + "Separator", Path.DirectorySeparatorChar.ToString());
                condition += $" AND instr(substr({alias}.path_key,{name}Start),{name}Separator)=0";
            }
            conditions.Add(condition + ")");
        }
        return "(" + string.Join(" OR ", conditions) + ")";
    }

    public static string Peer(SqliteCommand command, FolderTrackQuery query, bool earlier)
    {
        var scope = Scope(command, query, "peer", earlier ? "Earlier" : "Any");
        // Strict title/artist identity and sub-second duration matching; no audio hashing.
        return "EXISTS(SELECT 1 FROM tracks peer WHERE peer.missing=0 AND " + scope +
            " AND peer.id<>t.id AND echo_fold(peer.title)=echo_fold(t.title) AND echo_fold(peer.artist)=echo_fold(t.artist)" +
            " AND t.duration>0 AND abs(peer.duration-t.duration)<=0.5" +
            (earlier ? " AND (peer.path_key,peer.id)<(t.path_key,t.id)" : "") + ")";
    }

    public static string Insight(SqliteCommand command, FolderTrackQuery query) => query.Insight switch
    {
        FolderInsight.Recent => "t.created_at>=unixepoch()-30*86400", FolderInsight.Unplayed => Plays + "=0",
        FolderInsight.Lossless => Lossless, FolderInsight.HiRes => HiRes,
        FolderInsight.MissingCover => "EXISTS(SELECT 1 FROM folder_resource_facts f WHERE f.track_id=t.id AND f.cover=0)",
        FolderInsight.MissingLyrics => "EXISTS(SELECT 1 FROM folder_resource_facts f WHERE f.track_id=t.id AND f.lyrics=0)",
        FolderInsight.Duplicates => Peer(command, query, false), _ => "1=1"
    };

    public static string Order(SqliteCommand command, FolderTrackQuery query)
    {
        var columns = query.Sort switch
        {
            FolderTrackSort.Title => "t.sort_key", FolderTrackSort.Artist => "t.artist COLLATE NOCASE",
            FolderTrackSort.Album => "t.album COLLATE NOCASE", FolderTrackSort.Duration => "t.duration",
            FolderTrackSort.ArtistAlbum => "t.artist COLLATE NOCASE,t.album COLLATE NOCASE,t.disc_no,t.track_no",
            FolderTrackSort.TrackNumber => "t.disc_no,t.track_no", FolderTrackSort.Year => "t.year",
            FolderTrackSort.LastPlayed => LastPlayed, FolderTrackSort.PlayCount => Plays, FolderTrackSort.Bpm => Bpm,
            FolderTrackSort.Random => "((t.rowid%2147483647)*$randomSeed)%2147483647",
            FolderTrackSort.AudioSpec => "t.sample_rate,t.bit_depth", FolderTrackSort.Quality => Lossless + ",t.bit_depth,t.sample_rate",
            FolderTrackSort.Codec => "t.codec COLLATE NOCASE", FolderTrackSort.Bitrate => "CASE WHEN t.duration>0 THEN t.size_bytes*8.0/t.duration ELSE 0 END",
            FolderTrackSort.Created => "t.created_at", FolderTrackSort.Recent => "t.updated_at", FolderTrackSort.Modified => "t.modified_ticks",
            _ => "t.path_key"
        };
        if (query.Sort == FolderTrackSort.Random) command.Parameters.AddWithValue("$randomSeed", Math.Clamp(query.RandomSeed, 1, 65535));
        var direction = query.Descending ? " DESC" : " ASC";
        string[] fields = query.Sort switch
        {
            FolderTrackSort.ArtistAlbum => ["t.artist COLLATE NOCASE", "t.album COLLATE NOCASE", "t.disc_no", "t.track_no"],
            FolderTrackSort.TrackNumber => ["t.disc_no", "t.track_no"],
            FolderTrackSort.AudioSpec => ["t.sample_rate", "t.bit_depth"],
            FolderTrackSort.Quality => [Lossless, "t.bit_depth", "t.sample_rate"], _ => [columns]
        };
        return " ORDER BY " + string.Join(',', fields.Select(column => column + direction)) + ",t.id" + direction;
    }
}
