using System.IO.Compression;
using Microsoft.Data.Sqlite;

internal static class MigrationFixture
{
    public static void CreateV5(string sourcePath, string outputPath)
    {
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        source.Open(); using var probe = source.CreateCommand();
        probe.CommandText = "PRAGMA user_version";
        var version = Convert.ToInt64(probe.ExecuteScalar());
        if (version is not (6 or 7)) throw new InvalidOperationException("Use an isolated v6/v7 metadata fixture; found " + version);
        probe.CommandText = "SELECT count(*) FROM tracks WHERE source_json IS NOT NULL OR title NOT IN('Track 1','Track 2') OR artist!='Singer' OR album!='OriginalAlbum' OR album_artist!='OriginalArtist'";
        if (Convert.ToInt64(probe.ExecuteScalar()) != 0) throw new InvalidOperationException("Fixture must contain only generated local metadata.");
        var temporary = Path.GetFullPath(Path.Combine("misc", "architecture-validation", Guid.NewGuid().ToString("N") + "-v5.sqlite"));
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = temporary, Pooling = false }.ToString()))
        {
            target.Open(); source.BackupDatabase(target);
            using var command = target.CreateCommand();
            command.CommandText = """
                DROP TRIGGER album_identity_insert; DROP TRIGGER album_identity_file_update; DROP TRIGGER album_identity_delete;
                DROP TRIGGER album_identity_display_update; DROP TRIGGER album_metadata_insert;
                DROP TRIGGER album_metadata_update; DROP TRIGGER album_metadata_delete;
                DROP TABLE album_display_fts; DROP TABLE album_metadata_values; DROP TABLE album_identity;
                PRAGMA user_version=5; PRAGMA journal_mode=DELETE; VACUUM;
                """;
            command.ExecuteNonQuery();
            if (version == 7)
            {
                command.CommandText = "DROP INDEX tracks_source_audio; DROP INDEX source_cache_recent; ALTER TABLE tracks DROP COLUMN physical_path; ALTER TABLE tracks DROP COLUMN source_kind;";
                command.ExecuteNonQuery();
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        archive.CreateEntryFromFile(temporary, "library-v5.sqlite", CompressionLevel.SmallestSize);
        Console.WriteLine("Created generated-data-only v5 migration fixture: " + outputPath);
    }

    public static void ExtractV5(string path, string media)
    {
        using var archive = ZipFile.OpenRead("tests/Echo.Library.Smoke/Fixtures/library-v5.zip");
        using (var input = archive.GetEntry("library-v5.sqlite")!.Open())
        using (var output = File.Create(path)) input.CopyTo(output);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,title FROM tracks ORDER BY title";
        var rows = new List<(string Id, string Title)>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) rows.Add((reader.GetString(0), reader.GetString(1)));
        command.CommandText = "UPDATE tracks SET path=$path,path_key=$key WHERE id=$id";
        foreach (var row in rows)
        {
            var target = Path.Combine(media, row.Title.EndsWith('1') ? "1.flac" : "2.flac");
            command.Parameters.Clear(); command.Parameters.AddWithValue("$path", target);
            command.Parameters.AddWithValue("$key", Echo.Infrastructure.Files.LibraryPath.Key(target));
            command.Parameters.AddWithValue("$id", row.Id); command.ExecuteNonQuery();
        }
    }
}
