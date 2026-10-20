using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal static class ScanCreditTags
{
    public static void Write(SqliteConnection connection, SqliteTransaction transaction, string pathKey, IReadOnlyList<CreditTag>? tags)
    {
        ArtistRoleRules.Validate(tags);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.Parameters.AddWithValue("$path", pathKey);
        command.CommandText = "SELECT id FROM tracks WHERE path_key=$path"; var id = (string)command.ExecuteScalar()!;
        command.Parameters.Clear(); command.Parameters.AddWithValue("$id", id);
        command.CommandText = "DELETE FROM track_credit_tags WHERE track_id=$id"; command.ExecuteNonQuery();
        if (tags is null) return;
        command.CommandText = "INSERT INTO track_credit_tags VALUES($id,$ordinal,$tag,$value)";
        command.Parameters.AddWithValue("$ordinal", 0); command.Parameters.AddWithValue("$tag", ""); command.Parameters.AddWithValue("$value", "");
        for (var index = 0; index < tags.Count; index++)
        {
            command.Parameters[1].Value = index; command.Parameters[2].Value = ArtistRoleRules.TagKey(tags[index].Tag);
            command.Parameters[3].Value = tags[index].Value; command.ExecuteNonQuery();
        }
    }
}
