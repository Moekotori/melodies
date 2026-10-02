using Echo.Contracts.Library;
using Echo.Infrastructure.Search;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class LibraryFilterSql
{
    private const string KnownRates = "44100,48000,88200,96000,176400,192000,352800,384000";
    public static bool Active(LibraryFilter? filter) => filter is not null && filter != new LibraryFilter();
    public static void Validate(LibraryFilter? filter)
    {
        if (filter is null) return;
        if (!Enum.IsDefined(filter.Format) || !Enum.IsDefined(filter.Rate) || !Enum.IsDefined(filter.Source)
            || filter.BitDepth is < 1 or > 64 || filter.Year is < 1000 or > 2100 || filter.Genre?.Length > 128 || filter.Folder?.Length > 1024)
            throw new ArgumentException("Invalid library filter.");
    }
    public static string Build(SqliteCommand command, LibraryFilter? filter, string alias = "t")
    {
        Validate(filter); if (filter is null) return "";
        var conditions = new List<string>();
        string Bind(string name, object value) { command.Parameters.AddWithValue(name, value); return name; }
        var codec = $"upper(trim({alias}.codec))"; var path = $"lower({alias}.path)";
        var dsd = $"({codec} IN ('DSF','DFF','DSD') OR {alias}.sample_rate>=1000000)";
        string Format(FileFormat format) => format switch
        {
            FileFormat.Flac => $"({codec}='FLAC' OR {path} LIKE '%.flac')",
            FileFormat.Wav => $"({codec} IN ('WAV','WAVE','PCM') OR {path} LIKE '%.wav')",
            FileFormat.Aiff => $"({codec} IN ('AIFF','AIF','AIFC') OR {path} LIKE '%.aif' OR {path} LIKE '%.aiff' OR {path} LIKE '%.aifc')",
            FileFormat.Alac => $"{codec} IN ('ALAC','APPLE LOSSLESS')",
            FileFormat.Mp3 => $"({codec}='MP3' OR {codec} LIKE 'MPEG%LAYER 3%' OR {codec} LIKE 'MPEG%LAYER III%' OR {path} LIKE '%.mp3')",
            FileFormat.Aac => $"({codec} IN ('AAC','MP4A') OR {codec} LIKE 'AAC %' OR {path} LIKE '%.aac')",
            FileFormat.Ogg => $"({codec}='OGG' OR {codec} LIKE '%VORBIS%' OR {path} LIKE '%.ogg')",
            FileFormat.Opus => $"({codec}='OPUS' OR {path} LIKE '%.opus')", FileFormat.Dsd => dsd,
            _ => ""
        };
        if (filter.Format == FileFormat.Other)
            conditions.Add("NOT (" + string.Join(" OR ", Enum.GetValues<FileFormat>().Where(f => f is not (FileFormat.All or FileFormat.Other)).Select(Format)) + ")");
        else if (filter.Format != FileFormat.All) conditions.Add(Format(filter.Format));
        if (filter.Rate == AudioRate.Dsd) conditions.Add(dsd);
        else if (filter.Rate == AudioRate.Other) conditions.Add($"({alias}.sample_rate>0 AND {alias}.sample_rate NOT IN ({KnownRates}) AND NOT {dsd})");
        else if (filter.Rate != AudioRate.All)
        {
            int[] rates = [0,44100,48000,88200,96000,176400,192000,352800,384000];
            conditions.Add($"{alias}.sample_rate=" + Bind("$rate", rates[(int)filter.Rate]));
        }
        if (filter.BitDepth is { } depth) conditions.Add($"{alias}.bit_depth=" + Bind("$depth", depth));
        if (filter.Year is { } year) conditions.Add($"{alias}.year=" + Bind("$year", year));
        if (!string.IsNullOrWhiteSpace(filter.Genre)) conditions.Add($"echo_fold({alias}.genre)=" + Bind("$genre", LibrarySearchSchema.Fold(filter.Genre.Trim())));
        if (!string.IsNullOrWhiteSpace(filter.Folder))
        {
            var folder = filter.Folder.Trim().Replace('/', '\\').TrimEnd('\\') + '\\';
            conditions.Add($"replace(lower(coalesce(json_extract({alias}.source_json,'$.AudioPath'),{alias}.path)),'/','\\') LIKE " + Bind("$folder", LibrarySearchSql.Escape(folder.ToLowerInvariant()) + "%") + " ESCAPE '\\'");
        }
        if (filter.FavoriteOnly) conditions.Add($"{alias}.favorite=1");
        if (filter.LosslessOnly) conditions.Add($"{codec} IN ('FLAC','ALAC','WAV','WAVE','AIFF','AIF','AIFC','APE','WV','DSF','DFF','DSD','PCM')");
        if (filter.Source == LibrarySource.Files) conditions.Add($"{alias}.source_json IS NULL");
        else if (filter.Source != LibrarySource.All) conditions.Add($"lower(json_extract({alias}.source_json,'$.Kind'))=" + Bind("$source", filter.Source.ToString().ToLowerInvariant()));
        return conditions.Count == 0 ? "" : " AND " + string.Join(" AND ", conditions);
    }
}
