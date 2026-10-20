using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

internal sealed class ArtistRoleWriter : IDisposable
{
    private readonly SqliteCommand tags, track, clear, person, credit;
    private readonly ArtistCreditPolicy policy;
    public ArtistRoleWriter(SqliteConnection connection, SqliteTransaction transaction, ArtistCreditPolicy policy)
    {
        this.policy = policy;
        tags = Make("SELECT tag,value FROM track_credit_tags WHERE track_id=$id ORDER BY ordinal", "$id");
        track = Make("SELECT t.artist,coalesce(nullif(trim(t.album_artist),''),a.artist,t.album_credit) FROM tracks t LEFT JOIN albums a ON a.id=t.album_key WHERE t.id=$id", "$id");
        clear = Make("DELETE FROM person_credits WHERE track_id=$id", "$id");
        person = Make("INSERT OR IGNORE INTO credit_people VALUES($person,$name)", "$person", "$name");
        credit = Make("INSERT OR IGNORE INTO person_credits VALUES($id,$person,$role,$category,$scope)", "$id", "$person", "$role", "$category", "$scope");
        SqliteCommand Make(string sql, params string[] parameters)
        {
            var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
            foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter, "");
            command.Prepare(); return command;
        }
    }
    private List<CreditTag> Read(string id)
    {
        tags.Parameters[0].Value = id; var result = new List<CreditTag>();
        using var reader = tags.ExecuteReader(); while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1)));
        return result;
    }
    public string[] SongNames(string id, string fallback)
    {
        var values = Read(id).Where(tag => tag.Tag == "ARTIST").ToArray();
        return values.Length == 0 ? policy.Split(fallback) : values.SelectMany(tag => policy.Split(tag.Value)).DistinctBy(ArtistCredits.Key).ToArray();
    }
    public void Update(string id)
    {
        var values = Read(id);
        track.Parameters[0].Value = id;
        using (var reader = track.ExecuteReader())
        {
            if (!reader.Read()) return;
            if (!values.Any(tag => tag.Tag == "ARTIST")) values.Add(new("ARTIST", reader.GetString(0)));
            if (!values.Any(tag => tag.Tag is "ALBUMARTIST" or "ALBUMPERFORMER")) values.Add(new("ALBUMARTIST", reader.GetString(1)));
        }
        clear.Parameters[0].Value = id; clear.ExecuteNonQuery(); var count = 0;
        foreach (var tag in values)
        {
            var definition = ArtistRoleRules.Tag(tag.Tag);
            var text = tag.Value;
            if (tag.Tag == "PERSONNEL")
            {
                var split = text.LastIndexOf(" - ", StringComparison.Ordinal);
                if (split <= 0 || split + 3 >= text.Length) continue;
                var role = ArtistRoleRules.CanonicalRole(text[(split + 3)..].Trim());
                definition = (role, ArtistRoleRules.Category(role), CreditScope.Track); text = text[..split];
            }
            if (definition is not { } value) continue;
            foreach (var name in policy.Split(text))
            {
                if (++count > 256) throw new ArgumentException("单曲署名关系超过 256 项。");
                var key = ArtistCredits.Key(name);
                person.Parameters[0].Value = key; person.Parameters[1].Value = name; person.ExecuteNonQuery();
                object[] parameters = [id, key, value.Role, (int)value.Category, (int)value.Scope];
                for (var index = 0; index < parameters.Length; index++) credit.Parameters[index].Value = parameters[index];
                credit.ExecuteNonQuery();
            }
        }
    }
    public void Dispose() { tags.Dispose(); track.Dispose(); clear.Dispose(); person.Dispose(); credit.Dispose(); }
}
