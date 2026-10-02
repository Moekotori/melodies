using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// The lane owns the connection. Dispose unregisters before the next admitted query can start.
internal sealed class ReadCancellation : IDisposable
{
    private readonly CancellationTokenRegistration _registration;
    public ReadCancellation(SqliteConnection connection, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        _registration = cancellation.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
    }
    public void Dispose() => _registration.Dispose();
}
