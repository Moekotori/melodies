using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class ArtistRoleSchema
{
    public static void Migrate(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE artist_import_state(id INTEGER PRIMARY KEY CHECK(id=1),revision INTEGER NOT NULL);
            INSERT INTO artist_import_state VALUES(1,0);
            CREATE TABLE artist_delimiters(ordinal INTEGER PRIMARY KEY,value TEXT NOT NULL);
            INSERT INTO artist_delimiters VALUES(0,';');
            CREATE TABLE track_credit_tags(track_id TEXT NOT NULL,ordinal INTEGER NOT NULL,tag TEXT NOT NULL,value TEXT NOT NULL,
              PRIMARY KEY(track_id,ordinal)) WITHOUT ROWID;
            CREATE TABLE credit_people(id TEXT PRIMARY KEY,name TEXT NOT NULL);
            CREATE TABLE person_credits(track_id TEXT NOT NULL,person_id TEXT NOT NULL,role TEXT NOT NULL,category INTEGER NOT NULL,scope INTEGER NOT NULL,
              PRIMARY KEY(track_id,person_id,role,scope)) WITHOUT ROWID;
            CREATE INDEX person_credits_person ON person_credits(person_id,category,track_id);
            CREATE INDEX person_credits_category ON person_credits(category,person_id,track_id);
            CREATE TRIGGER person_credit_delete AFTER DELETE ON person_credits BEGIN
              DELETE FROM credit_people WHERE id=old.person_id AND NOT EXISTS(SELECT 1 FROM person_credits WHERE person_id=old.person_id);
            END;
            CREATE TRIGGER credit_track_delete AFTER DELETE ON tracks BEGIN
              DELETE FROM track_credit_tags WHERE track_id=old.id;
              DELETE FROM person_credits WHERE track_id=old.id;
            END;
            """;
        command.ExecuteNonQuery();
        using var roles = new ArtistRoleWriter(connection, transaction, new([";"]));
        command.Parameters.AddWithValue("$after", 0L);
        while (true)
        {
            command.CommandText = "SELECT rowid,id FROM tracks WHERE rowid>$after AND missing=0 ORDER BY rowid LIMIT 200";
            var rows = new List<(long Row, string Id)>(200);
            using (var reader = command.ExecuteReader()) while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1)));
            if (rows.Count == 0) break;
            foreach (var row in rows) roles.Update(row.Id);
            command.Parameters[0].Value = rows[^1].Row;
        }
        command.CommandText = "PRAGMA user_version=28"; command.ExecuteNonQuery(); transaction.Commit();
    }
}
