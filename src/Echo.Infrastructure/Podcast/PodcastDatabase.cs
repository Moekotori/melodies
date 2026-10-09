using System.Globalization;
using System.Text.Json;
using Echo.Contracts.Podcast;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Podcast;

internal sealed class PodcastDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    private readonly object gate = new();
    private bool disposed;

    public PodcastDatabase(string path)
    {
        if (path != ":memory:") Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 1 }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA cache_size=-1024; PRAGMA busy_timeout=1000; PRAGMA foreign_keys=ON;
            CREATE TABLE IF NOT EXISTS podcast_feeds (id TEXT PRIMARY KEY, data TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS podcast_episodes (
              id TEXT PRIMARY KEY, feed_id TEXT NOT NULL REFERENCES podcast_feeds(id) ON DELETE CASCADE,
              data TEXT NOT NULL, published_at TEXT, position REAL NOT NULL DEFAULT 0, played INTEGER NOT NULL DEFAULT 0,
              source_order INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS podcast_episode_feed ON podcast_episodes(feed_id, published_at DESC);
            CREATE TABLE IF NOT EXISTS podcast_http (
              feed_id TEXT PRIMARY KEY REFERENCES podcast_feeds(id) ON DELETE CASCADE, etag TEXT, last_modified TEXT);
            """;
        command.ExecuteNonQuery();
        EnsureOrderColumn();
    }

    public PodcastFeed? Feed(string id) { lock (gate) return ReadFeed(id); }
    public PodcastValidators Validators(string id) { lock (gate) return ReadValidators(id); }

    public IReadOnlyList<PodcastFeed> List()
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT data FROM podcast_feeds ORDER BY json_extract(data, '$.title') LIMIT $limit";
            command.Parameters.AddWithValue("$limit", PodcastLimits.Feeds);
            using var rows = command.ExecuteReader();
            var feeds = new List<PodcastFeed>();
            while (rows.Read()) feeds.Add(JsonSerializer.Deserialize(rows.GetString(0), PodcastJsonContext.Default.PodcastFeed)!);
            return feeds;
        }
    }

    public PodcastFeed Import(string xml, string url, string documentUrl, string? existingId, PodcastValidators? validators)
    {
        var parsed = PodcastFeedParser.Parse(xml, url, documentUrl);
        lock (gate)
        {
            using var tx = connection.BeginTransaction();
            var existing = ReadFeed(parsed.Feed.Id);
            if (existingId is not null && (existingId != parsed.Feed.Id || existing is null)) throw new PodcastFailure("not_found");
            if (existing is null && CountFeeds() >= PodcastLimits.Feeds) throw new PodcastFailure("feed_limit");
            var feed = parsed.Feed;
            if (existing is not null)
            {
                var stored = StoredFeed(existing.Id);
                var candidate = JsonSerializer.Serialize(feed with { UpdatedAt = existing.UpdatedAt }, PodcastJsonContext.Default.PodcastFeed);
                feed = candidate == stored ? existing : feed;
            }
            Exec("""
                INSERT INTO podcast_feeds(id, data) VALUES ($id, $data) ON CONFLICT(id) DO UPDATE SET data = excluded.data
                WHERE data IS NOT excluded.data
                """, ("$id", feed.Id), ("$data", JsonSerializer.Serialize(feed, PodcastJsonContext.Default.PodcastFeed)));
            string? previous = null;
            var started = false;
            var order = 0;
            foreach (var episode in parsed.Episodes)
            {
                order = started && previous == episode.PublishedAt ? order + 1 : 0;
                started = true; previous = episode.PublishedAt;
                Exec("""
                    INSERT INTO podcast_episodes(id, feed_id, data, published_at, source_order) VALUES ($id, $feed, $data, $published, $order)
                    ON CONFLICT(id) DO UPDATE SET data = excluded.data, published_at = excluded.published_at, source_order = excluded.source_order
                    WHERE data IS NOT excluded.data OR published_at IS NOT excluded.published_at OR source_order IS NOT excluded.source_order
                    """, ("$id", episode.Id), ("$feed", episode.FeedId),
                    ("$data", JsonSerializer.Serialize(episode, PodcastJsonContext.Default.PodcastEpisode)),
                    ("$published", (object?)episode.PublishedAt ?? DBNull.Value), ("$order", order));
            }
            Exec("""
                DELETE FROM podcast_episodes WHERE feed_id = $id AND id NOT IN (
                  SELECT id FROM podcast_episodes WHERE feed_id = $id ORDER BY published_at DESC, source_order ASC, rowid DESC LIMIT $limit)
                """, ("$id", feed.Id), ("$limit", PodcastLimits.Episodes));
            if (validators is { } saved) WriteValidators(feed.Id, saved);
            tx.Commit();
            return feed;
        }
    }

    public PodcastFeed NotModified(string id, PodcastValidators validators)
    {
        lock (gate)
        {
            var feed = ReadFeed(id) ?? throw new PodcastFailure("not_found");
            WriteValidators(id, validators.Or(ReadValidators(id)));
            return feed;
        }
    }

    public void Remove(string id) { lock (gate) Exec("DELETE FROM podcast_feeds WHERE id = $id", ("$id", id)); }

    public PodcastEpisodePage Episodes(PodcastEpisodeQuery query)
    {
        lock (gate)
        {
            var search = "%" + Escape(query.Search.Length > 128 ? query.Search[..128] : query.Search) + "%";
            var state = query.Filter switch
            {
                PodcastEpisodeFilter.InProgress => " AND played = 0 AND position > 0",
                PodcastEpisodeFilter.Unplayed => " AND played = 0",
                PodcastEpisodeFilter.Played => " AND played = 1",
                _ => ""
            };
            var where = "feed_id = $feed AND json_extract(data, '$.title') LIKE $search ESCAPE '\\'" + state;
            var total = Scalar<long>($"SELECT count(*) FROM podcast_episodes WHERE {where}", ("$feed", query.FeedId), ("$search", search));
            var pages = Math.Max(0, (int)Math.Ceiling(total / (double)PodcastLimits.PageSize) - 1);
            var page = Math.Clamp(query.Page, 0, pages);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT data, position, played FROM podcast_episodes WHERE {where} ORDER BY published_at DESC, source_order ASC, rowid DESC LIMIT $limit OFFSET $offset";
            command.Parameters.AddWithValue("$feed", query.FeedId);
            command.Parameters.AddWithValue("$search", search);
            command.Parameters.AddWithValue("$limit", PodcastLimits.PageSize);
            command.Parameters.AddWithValue("$offset", page * PodcastLimits.PageSize);
            using var rows = command.ExecuteReader();
            var items = new List<PodcastEpisode>();
            while (rows.Read())
            {
                var episode = JsonSerializer.Deserialize(rows.GetString(0), PodcastJsonContext.Default.PodcastEpisode)!;
                items.Add(episode with { Position = rows.GetDouble(1), Played = rows.GetInt64(2) == 1 });
            }
            return new(items, (int)total, page);
        }
    }

    public PodcastEpisode? Episode(string id)
    {
        lock (gate)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT data, position, played FROM podcast_episodes WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            using var rows = command.ExecuteReader();
            if (!rows.Read()) return null;
            var episode = JsonSerializer.Deserialize(rows.GetString(0), PodcastJsonContext.Default.PodcastEpisode)!;
            return episode with { Position = rows.GetDouble(1), Played = rows.GetInt64(2) == 1 };
        }
    }

    public void Progress(string id, double position, bool ended)
    {
        if (!double.IsFinite(position) || position < 0) return;
        var capped = Math.Min(position, 7 * 86400);
        lock (gate) Exec("""
            UPDATE podcast_episodes SET position = $position, played = CASE WHEN $ended THEN 1 ELSE played END
            WHERE id = $id AND (position IS NOT $position OR ($ended AND played = 0))
            """, ("$position", capped), ("$ended", ended ? 1 : 0), ("$id", id));
    }

    public void MarkPlayed(string id, bool played)
    { lock (gate) Exec("UPDATE podcast_episodes SET played = $played, position = 0 WHERE id = $id", ("$played", played ? 1 : 0), ("$id", id)); }

    public long Changes() { lock (gate) return Scalar<long>("SELECT total_changes()"); }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; connection.Dispose(); }
    }

    private void EnsureOrderColumn()
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info(podcast_episodes)";
        using var rows = command.ExecuteReader();
        var found = false;
        while (rows.Read()) if (rows.GetString(1) == "source_order") found = true;
        if (!found) Exec("ALTER TABLE podcast_episodes ADD COLUMN source_order INTEGER NOT NULL DEFAULT 0");
    }

    private PodcastFeed? ReadFeed(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM podcast_feeds WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        var data = command.ExecuteScalar() as string;
        return data is null ? null : JsonSerializer.Deserialize(data, PodcastJsonContext.Default.PodcastFeed);
    }

    private string StoredFeed(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT data FROM podcast_feeds WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        return (string)command.ExecuteScalar()!;
    }

    private int CountFeeds() => (int)Scalar<long>("SELECT count(*) FROM podcast_feeds");

    private PodcastValidators ReadValidators(string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT etag, last_modified FROM podcast_http WHERE feed_id = $id";
        command.Parameters.AddWithValue("$id", id);
        using var rows = command.ExecuteReader();
        return rows.Read() ? new(rows.IsDBNull(0) ? null : rows.GetString(0), rows.IsDBNull(1) ? null : rows.GetString(1)) : new(null, null);
    }

    private void WriteValidators(string id, PodcastValidators validators) => Exec("""
        INSERT INTO podcast_http(feed_id, etag, last_modified) VALUES ($id, $etag, $modified)
        ON CONFLICT(feed_id) DO UPDATE SET etag = excluded.etag, last_modified = excluded.last_modified
        WHERE etag IS NOT excluded.etag OR last_modified IS NOT excluded.last_modified
        """, ("$id", id), ("$etag", (object?)validators.Etag ?? DBNull.Value), ("$modified", (object?)validators.LastModified ?? DBNull.Value));

    private void Exec(string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return (T)Convert.ChangeType(command.ExecuteScalar(), typeof(T), CultureInfo.InvariantCulture)!;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}
