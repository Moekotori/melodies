using Echo.Infrastructure.Library;
using Echo.Infrastructure.Search;
using Microsoft.Data.Sqlite;

internal static class StressQueryPlans
{
    public static void Capture(string root, string label)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = StressLibraryFixture.Database(root), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        connection.CreateFunction<string, string>("echo_fold", LibrarySearchSchema.Fold, isDeterministic: true);
        connection.CreateFunction("echo_album_key", (string? path, string? album, string? artist, string? kind, string? json)
            => AlbumGroupingKey.Compute(path, album, artist, kind, json), isDeterministic: true);
        using var log = new StreamWriter(Path.Combine(root, label + "-plans.txt"));
        Plan("songs-deep", """
            SELECT t.id,t.title,coalesce((SELECT display_title FROM album_identity WHERE group_key=t.album_key),t.album)
            FROM tracks t WHERE t.missing=0
            ORDER BY t.sort_key ASC,t.artist COLLATE NOCASE ASC,t.id ASC LIMIT 201 OFFSET 399800
            """);
        Plan("artists-deep", "SELECT a.id,a.name FROM artists a WHERE a.grouping=0 ORDER BY a.sort_key,a.id LIMIT 200 OFFSET 99800");
        Plan("artists-search", "SELECT a.id FROM artists a WHERE a.grouping=0 AND echo_fold(a.name) LIKE '%099999%' ORDER BY a.sort_key,a.id LIMIT 200");
        Plan("albums-deep", """
            SELECT i.stable_id,i.display_title FROM albums a JOIN album_identity i ON i.group_key=a.id
            ORDER BY i.title_key,i.stable_id LIMIT 200 OFFSET 99800
            """);
        log.WriteLine("Actual database / index sizes:");
        using var size = connection.CreateCommand();
        size.CommandText = "SELECT name,sum(pgsize) bytes FROM dbstat GROUP BY name ORDER BY bytes DESC LIMIT 25";
        using var reader = size.ExecuteReader();
        while (reader.Read()) log.WriteLine(reader.GetString(0) + ": " + reader.GetInt64(1));

        void Plan(string name, string sql)
        {
            log.WriteLine(name);
            using var command = connection.CreateCommand(); command.CommandText = "EXPLAIN QUERY PLAN " + sql;
            using var rows = command.ExecuteReader();
            while (rows.Read()) log.WriteLine(rows.GetString(3));
        }
    }
}
