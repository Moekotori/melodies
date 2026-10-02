using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

public sealed partial class LibraryDatabase : ILibraryMaintenance
{
    public Task<LibraryHealth> CheckAsync(CancellationToken cancellation) => _interactiveReader.QueueAsync(connection =>
    {
        using var integrity = connection.CreateCommand(); integrity.CommandText = "PRAGMA quick_check";
        var result = integrity.ExecuteScalar()?.ToString() ?? "unknown";
        using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*),SUM(missing),0 FROM tracks";
        using var reader = command.ExecuteReader(); reader.Read();
        return new LibraryHealth(result, reader.GetInt64(0), reader.IsDBNull(1) ? 0 : reader.GetInt64(1), reader.IsDBNull(2) ? 0 : reader.GetInt64(2));
    }, true, cancellation);
    public Task BackupAsync(string destination, CancellationToken cancellation) => _writer.QueueAsync(connection =>
    {
        var path = Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (string.Equals(path, Path.GetFullPath(connection.DataSource), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("备份目标不能覆盖当前数据库。");
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        target.Open(); connection.BackupDatabase(target); return 0;
    }, true, cancellation);
}
