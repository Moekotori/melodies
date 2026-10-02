using Echo.Contracts.Library;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Search;

internal static class AlbumSearchSql
{
    public static string Build(SqliteCommand command, string? input, LibraryFilter? filter)
    {
        var parsed = LibrarySearchParser.Parse(input ?? "");
        // Plain album text searches canonical display metadata. Commands constrain one member track.
        var text = string.Join(' ', parsed.Terms);
        var sql = LibrarySearchSql.Entity(command, text, "i.display_title", "i.display_artist");
        var track = LibraryFilterSql.Build(command, filter);
        if (filter?.FavoriteOnly == true)
            track = track.Replace(" AND t.favorite=1", " AND coalesce((SELECT favorite FROM album_user_state WHERE album_id=a.id),0)=1");
        var commands = parsed.Fields.Count > 0 || parsed.Exact.Count > 0 || parsed.Exclude.Count > 0 || parsed.Year is not null || parsed.Decade is not null || parsed.Format is not null;
        if (commands) track += LibrarySearchSql.Tracks(command, input, includeTerms: false);
        if (track.Length > 0) sql += " AND EXISTS(SELECT 1 FROM tracks t WHERE t.album_key=a.id AND t.missing=0" + track + ")";
        return sql;
    }
}
