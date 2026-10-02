using Echo.Infrastructure.Library;
using Echo.Playback;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Playback;

/// <summary>Independent new-app state DB. One lane owns all access; never opens a legacy music library.</summary>
public sealed class PlaybackStateDatabase : IPlaybackCheckpointStore
{
    private readonly FileStream owner;
    private readonly SqliteLane lane;
    private PlaybackStateDatabase(FileStream owner, SqliteLane lane) { this.owner = owner; this.lane = lane; }
    public static async Task<PlaybackStateDatabase> OpenAsync(string file, CancellationToken token = default)
    {
        var path = Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var owner = new FileStream(path + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        SqliteLane? lane = null;
        try
        {
            lane = new(path, false, Initialize);
            await lane.Ready.WaitAsync(token).ConfigureAwait(false);
            return new(owner, lane);
        }
        catch { if (lane is not null) try { await lane.DisposeAsync(); } catch { } owner.Dispose(); throw; }
    }
    private static void Initialize(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA application_id";
        var id = Convert.ToInt64(command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name NOT LIKE 'sqlite_%'";
        var tables = Convert.ToInt64(command.ExecuteScalar());
        if (id != 1162039362 && (id != 0 || tables != 0)) throw new IOException("不是 ECHO Avalonia 播放状态库");
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt64(command.ExecuteScalar()) > 1) throw new IOException("播放状态库版本过高");
        command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA application_id=1162039362; PRAGMA user_version=1; CREATE TABLE IF NOT EXISTS playback_state(id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }
    public Task<string?> ReadAsync(CancellationToken token = default) => lane.QueueAsync(connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM playback_state WHERE id=1";
        var text = command.ExecuteScalar() as string;
        if (text?.Length > 512 * 1024) throw new IOException("播放状态过大");
        return text;
    }, true, token);
    public Task SaveAsync(string json, CancellationToken token = default)
    {
        if (json.Length > 512 * 1024) throw new ArgumentException("播放状态过大");
        return lane.QueueAsync(connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO playback_state(id,payload) VALUES(1,$p) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
            command.Parameters.AddWithValue("$p", json);
            return command.ExecuteNonQuery();
        }, true, token);
    }
    public async ValueTask DisposeAsync() { try { await lane.DisposeAsync(); } finally { owner.Dispose(); } }
}
