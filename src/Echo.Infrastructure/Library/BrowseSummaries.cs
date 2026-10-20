using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase
{
    // Owned by the interactive reader. Retain aggregates only, never track rows or artwork.
    private HomeLibrarySummary? _homeSummary;
    private long _homeSummaryRevision = -1;
    private readonly Dictionary<FolderSummaryKey, FolderSummary> _folderSummaries = [];
    private long _folderSummaryRevision = -1;

    private HomeLibrarySummary ReadHomeSummary(SqliteCommand command)
    {
        command.CommandText = "SELECT revision FROM library_state WHERE id=1";
        var revision = (long)command.ExecuteScalar()!;
        if (_homeSummaryRevision == revision && _homeSummary is { } cached) return cached;
        command.Connection!.CreateFunction<string, string>("echo_home_folder",
            path => Path.GetDirectoryName(path) ?? "", isDeterministic: true);
        command.CommandText = """
            SELECT (SELECT count(*) FROM artists WHERE grouping=0),
              (SELECT count(*) FROM albums),(SELECT count(*) FROM tracks WHERE missing=0),
              (SELECT count(DISTINCT echo_home_folder(path)) FROM tracks WHERE missing=0)
            """;
        using var reader = command.ExecuteReader();
        reader.Read();
        var summary = new HomeLibrarySummary(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3));
        _homeSummary = summary; _homeSummaryRevision = revision;
        return summary;
    }

    private FolderSummary ReadFolderSummary(SqliteCommand command, FolderTrackQuery query,
        string prefix, string where, long revision)
    {
        if (_folderSummaryRevision != revision)
        { _folderSummaries.Clear(); _folderSummaryRevision = revision; }
        // Recent membership changes with wall time; resource facts have their own write lifecycle.
        // Combined-folder queries stay uncached so up to 32 long paths never become resident keys.
        var cacheable = query.Paths is not { Count: > 0 } && query.Insight is
            FolderInsight.All or FolderInsight.Unplayed or FolderInsight.Lossless or FolderInsight.HiRes or FolderInsight.Duplicates;
        var key = new FolderSummaryKey(prefix, query.Search, query.Recursive, query.LosslessOnly,
            query.HideDuplicates, query.Insight);
        if (cacheable && _folderSummaries.TryGetValue(key, out var cached)) return cached;
        command.CommandText = "SELECT count(*),coalesce(sum(t.duration),0),coalesce(sum(t.size_bytes),0),coalesce(sum(" + FolderLossless + "),0)" + where;
        using var reader = command.ExecuteReader();
        reader.Read();
        var summary = new FolderSummary(reader.GetInt64(0), reader.GetDouble(1), reader.GetInt64(2), reader.GetInt64(3));
        if (cacheable)
        {
            if (_folderSummaries.Count >= 16) _folderSummaries.Remove(_folderSummaries.Keys.First());
            _folderSummaries[key] = summary;
        }
        return summary;
    }

    private readonly record struct FolderSummaryKey(string Prefix, string? Search, bool Recursive,
        bool LosslessOnly, bool HideDuplicates, FolderInsight Insight);
    private readonly record struct FolderSummary(long Total, double Duration, long Bytes, long Lossless);
}
