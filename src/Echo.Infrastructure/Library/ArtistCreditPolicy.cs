using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal sealed class ArtistCreditPolicy
{
    public string[] Delimiters { get; }
    public ArtistCreditPolicy(IReadOnlyList<string> delimiters)
    {
        if (delimiters.Count > 16 || delimiters.Any(value => value.Length is < 1 or > 16 || value.Contains('\0') || value.Contains('\n') || value.Contains('\r')))
            throw new ArgumentException("最多使用 16 个分隔符，每个 1–16 字符；NUL 始终保留。");
        Delimiters = delimiters.Select(value => value.Normalize(System.Text.NormalizationForm.FormKC)).Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length).ToArray();
    }
    public string[] Split(string value)
    {
        value = value.Normalize(System.Text.NormalizationForm.FormKC);
        var names = new List<string>(4); var keys = new HashSet<string>(StringComparer.Ordinal);
        var start = 0;
        for (var position = 0; position <= value.Length;)
        {
            var length = position < value.Length && value[position] == '\0' ? 1 : 0;
            if (length == 0 && position < value.Length)
                foreach (var delimiter in Delimiters)
                    if (value.AsSpan(position).StartsWith(delimiter, StringComparison.Ordinal)) { length = delimiter.Length; break; }
            if (position < value.Length && length == 0) { position++; continue; }
            var name = ArtistCredits.Display(value[start..position]);
            if (name.Length > 0 && keys.Add(ArtistCredits.Key(name)))
            {
                if (names.Count == 64) return [ArtistCredits.Display(value)];
                names.Add(name);
            }
            if (position == value.Length) break;
            position += length; start = position;
        }
        return names.ToArray();
    }
    public static bool Exists(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='artist_import_state'";
        return (long)command.ExecuteScalar()! != 0;
    }
    public static ArtistImportSettings Read(SqliteConnection connection, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT revision FROM artist_import_state WHERE id=1"; var revision = (long)command.ExecuteScalar()!;
        command.CommandText = "SELECT value FROM artist_delimiters ORDER BY ordinal";
        var values = new List<string>(16); using var reader = command.ExecuteReader();
        while (reader.Read()) values.Add(reader.GetString(0));
        return new(revision, values);
    }
}
