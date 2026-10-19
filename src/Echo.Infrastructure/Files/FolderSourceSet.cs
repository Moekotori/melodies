using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Files;

// Scratch facts owned by one scan. Large CUE libraries must not retain every audio path in RAM.
internal sealed class FolderSourceSet : IDisposable
{
    private readonly HashSet<string> _memory = new(StringComparer.Ordinal);
    private int _characters;
    private string? _path;
    private SqliteConnection? _disk;
    private SqliteTransaction? _transaction;
    private SqliteCommand? _add, _contains;

    public void Add(string key)
    {
        if (_disk is null && (_memory.Count >= 1024 || _characters + key.Length > 128 * 1024)) Spill();
        if (_disk is null)
        {
            if (_memory.Add(key)) _characters += key.Length;
            return;
        }
        _add!.Parameters[0].Value = key;
        _add.ExecuteNonQuery();
    }

    public bool Contains(string key)
    {
        if (_disk is null) return _memory.Contains(key);
        _contains!.Parameters[0].Value = key;
        return _contains.ExecuteScalar() is not null;
    }

    private void Spill()
    {
        _path = Path.Combine(Path.GetTempPath(), "echo-folder-" + Guid.NewGuid().ToString("N") + ".sqlite");
        _disk = new(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ToString());
        _disk.Open();
        using var setup = _disk.CreateCommand();
        // Disposable scratch only: no durability is needed, and the library WAL remains untouched.
        setup.CommandText = "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF; PRAGMA cache_size=-1024; PRAGMA mmap_size=0; CREATE TABLE sources(key TEXT PRIMARY KEY) WITHOUT ROWID;";
        setup.ExecuteNonQuery();
        _add = _disk.CreateCommand();
        _add.CommandText = "INSERT OR IGNORE INTO sources VALUES($key)";
        _add.Parameters.Add(new("$key", SqliteType.Text)); _add.Prepare();
        _contains = _disk.CreateCommand();
        _contains.CommandText = "SELECT 1 FROM sources WHERE key=$key";
        _contains.Parameters.Add(new("$key", SqliteType.Text)); _contains.Prepare();
        // Scratch never needs a durable commit. One transaction avoids an autocommit per source;
        // SQLite still spills dirty pages within the 1 MiB page-cache budget.
        _transaction = _disk.BeginTransaction();
        _add.Transaction = _transaction; _contains.Transaction = _transaction;
        foreach (var key in _memory) { _add.Parameters[0].Value = key; _add.ExecuteNonQuery(); }
        _memory.Clear(); _memory.TrimExcess(); _characters = 0;
    }

    public void Dispose()
    {
        _add?.Dispose(); _contains?.Dispose(); _transaction?.Dispose(); _disk?.Dispose();
        if (_path is not null)
        {
            try { File.Delete(_path); }
            catch (IOException) { /* OS temp maintenance can remove a scratch file held by an external scanner. */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
