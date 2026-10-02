using System.Text;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Search;

internal static class LibrarySearchSchema
{
    public static string Fold(string value) => value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        var fields = new[] { "title", "artist", "album", "album_artist", "genre", "path" };
        var columns = string.Join(',', fields);
        var select = string.Join(',', fields.Select(field => $"echo_fold({field})"));
        var values = string.Join(',', fields.Select(field => $"echo_fold(new.{field})"));
        var sql = new StringBuilder();
        foreach (var (table, tokenizer) in new[] { ("library_search", "unicode61 remove_diacritics 2"), ("library_substrings", "trigram") })
        {
            sql.Append($"CREATE VIRTUAL TABLE {table} USING fts5({columns},tokenize='{tokenizer}');");
            sql.Append($"INSERT INTO {table}(rowid,{columns}) SELECT rowid,{select} FROM tracks;");
            sql.Append($"CREATE TRIGGER {table}_insert AFTER INSERT ON tracks BEGIN INSERT INTO {table}(rowid,{columns}) VALUES(new.rowid,{values}); END;");
            sql.Append($"CREATE TRIGGER {table}_delete AFTER DELETE ON tracks BEGIN DELETE FROM {table} WHERE rowid=old.rowid; END;");
            sql.Append($"CREATE TRIGGER {table}_update AFTER UPDATE OF {columns} ON tracks BEGIN DELETE FROM {table} WHERE rowid=old.rowid; INSERT INTO {table}(rowid,{columns}) VALUES(new.rowid,{values}); END;");
        }
        sql.Append("""
            ALTER TABLE tracks ADD COLUMN created_at INTEGER NOT NULL DEFAULT 0;
            UPDATE tracks SET created_at=updated_at;
            CREATE TRIGGER tracks_created AFTER INSERT ON tracks WHEN new.created_at=0 BEGIN
              UPDATE tracks SET created_at=new.updated_at WHERE rowid=new.rowid; END;
            CREATE INDEX tracks_artist_page ON tracks(artist COLLATE NOCASE,id) WHERE missing=0;
            CREATE INDEX tracks_duration_page ON tracks(duration,id) WHERE missing=0;
            CREATE INDEX tracks_year_page ON tracks(year,id) WHERE missing=0;
            CREATE INDEX tracks_recent_page ON tracks(updated_at,id) WHERE missing=0;
            CREATE INDEX tracks_created_page ON tracks(created_at,id) WHERE missing=0;
            CREATE INDEX tracks_modified_page ON tracks(modified_ticks,id) WHERE missing=0;
            CREATE INDEX tracks_rate_page ON tracks(sample_rate,id) WHERE missing=0;
            CREATE INDEX tracks_depth_page ON tracks(bit_depth,id) WHERE missing=0;
            CREATE INDEX tracks_codec_page ON tracks(codec COLLATE NOCASE,id) WHERE missing=0;
            PRAGMA user_version=8;
            """);
        command.CommandText = sql.ToString(); command.ExecuteNonQuery(); transaction.Commit();
    }
}
