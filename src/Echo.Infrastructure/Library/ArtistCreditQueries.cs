using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : IArtistCreditLibrary
{
    public Task<ArtistImportSettings> GetArtistImportSettingsAsync(CancellationToken cancellation = default)
        => _interactiveReader.QueueAsync(connection => ArtistCreditPolicy.Read(connection), true, cancellation);
    public Task<PersonPage> GetPeopleAsync(PersonQuery query, CancellationToken cancellation = default)
    {
        CreditBounds(query.Offset, query.Limit, query.Category);
        if (query.Search?.Length > 128) throw new ArgumentException("人员搜索最多 128 字符。");
        return ReadCredits((command, revision) =>
        {
            var where = " FROM credit_people p WHERE EXISTS(SELECT 1 FROM person_credits c JOIN tracks t ON t.id=c.track_id WHERE c.person_id=p.id AND t.missing=0" + CategorySql(query.Category) + ")";
            if (!string.IsNullOrEmpty(query.Search))
            { where += " AND instr(p.id,$search)>0"; command.Parameters.AddWithValue("$search", ArtistCredits.Key(query.Search)); }
            BindCategory(command, query.Category);
            command.CommandText = "SELECT count(*)" + where; var total = Convert.ToInt32(command.ExecuteScalar());
            command.CommandText = """
                SELECT p.id,p.name,
                  (SELECT count(DISTINCT c.track_id) FROM person_credits c JOIN tracks t ON t.id=c.track_id WHERE c.person_id=p.id AND t.missing=0),
                  (SELECT group_concat(role) FROM (SELECT DISTINCT c.role FROM person_credits c JOIN tracks t ON t.id=c.track_id WHERE c.person_id=p.id AND t.missing=0 ORDER BY c.role LIMIT 16))
                """ + where + " ORDER BY p.name COLLATE NOCASE,p.id LIMIT $limit OFFSET $offset";
            PageParameters(command, query.Offset, query.Limit);
            var items = new List<CreditPerson>(query.Limit); using var reader = command.ExecuteReader();
            while (reader.Read()) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3)));
            return new PersonPage(items, total, revision);
        }, query.Revision, cancellation);
    }
    public Task<CreditPage> GetTrackCreditsAsync(string trackId, int offset = 0, int limit = 100, CancellationToken cancellation = default)
    {
        CreditBounds(offset, limit, null); CreditId(trackId);
        return ReadCredits((command, revision) =>
        {
            command.Parameters.AddWithValue("$id", trackId);
            var from = " FROM person_credits c JOIN credit_people p ON p.id=c.person_id JOIN tracks t ON t.id=c.track_id WHERE c.track_id=$id AND t.missing=0";
            command.CommandText = "SELECT count(*)" + from; var total = Convert.ToInt32(command.ExecuteScalar());
            command.CommandText = "SELECT p.id,p.name,c.role,c.category,c.scope" + from + " ORDER BY c.scope DESC,c.category,p.name,c.role LIMIT $limit OFFSET $offset";
            PageParameters(command, offset, limit);
            var items = new List<PersonCredit>(limit); using var reader = command.ExecuteReader();
            while (reader.Read()) items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), (CreditCategory)reader.GetInt32(3), (CreditScope)reader.GetInt32(4)));
            return new CreditPage(items, total, revision);
        }, null, cancellation);
    }
    public Task<CreditAppearancePage> GetPersonCreditsAsync(PersonCreditQuery query, CancellationToken cancellation = default)
    {
        CreditBounds(query.Offset, query.Limit, query.Category); CreditId(query.PersonId);
        return ReadCredits((command, revision) =>
        {
            command.Parameters.AddWithValue("$id", query.PersonId); BindCategory(command, query.Category);
            var from = " FROM person_credits c JOIN tracks t ON t.id=c.track_id WHERE c.person_id=$id AND t.missing=0" + CategorySql(query.Category);
            command.CommandText = "SELECT count(*)" + from; var total = Convert.ToInt32(command.ExecuteScalar());
            command.CommandText = "SELECT t.id,t.path,t.title,t.artist,t.album,t.duration,t.codec,t.sample_rate,t.bit_depth,t.sort_key,t.track_no,t.disc_no,t.favorite,t.source_json,c.role,c.category,c.scope"
                + from + " ORDER BY t.sort_key,t.id,c.role,c.scope LIMIT $limit OFFSET $offset";
            PageParameters(command, query.Offset, query.Limit);
            var items = new List<CreditAppearance>(query.Limit); using var reader = command.ExecuteReader();
            while (reader.Read()) items.Add(new(TrackRowMapping.Read(reader), reader.GetString(14), (CreditCategory)reader.GetInt32(15), (CreditScope)reader.GetInt32(16)));
            return new CreditAppearancePage(items, total, revision);
        }, query.Revision, cancellation);
    }
    private Task<T> ReadCredits<T>(Func<SqliteCommand,long,T> read, long? expected, CancellationToken token)
        => _interactiveReader.QueueAsync(connection =>
        {
            using var interrupt = new ReadCancellation(connection, token);
            using var transaction = connection.BeginTransaction(deferred: true);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT revision FROM library_state WHERE id=1"; var revision = (long)command.ExecuteScalar()!;
            if (expected is { } value && value != revision) throw new LibraryChangedException();
            return read(command, revision);
        }, true, token);
    private static void CreditBounds(int offset, int limit, CreditCategory? category)
    { if (offset < 0 || limit is < 1 or > 100 || category is { } value && !Enum.IsDefined(value)) throw new ArgumentException("无效的署名分页请求。"); }
    private static void CreditId(string id) { if (id.Length is < 1 or > 16384) throw new ArgumentException("无效的人员或曲目标识。"); }
    private static string CategorySql(CreditCategory? category) => category is null ? "" : " AND c.category=$category";
    private static void BindCategory(SqliteCommand command, CreditCategory? category) { if (category is { } value) command.Parameters.AddWithValue("$category", (int)value); }
    private static void PageParameters(SqliteCommand command, int offset, int limit)
    { command.Parameters.AddWithValue("$offset", offset); command.Parameters.AddWithValue("$limit", limit); }
}
