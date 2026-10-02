using System.Threading.Channels;
using Echo.Contracts.Library;
using Microsoft.Data.Sqlite;

namespace Echo.Infrastructure.Library;

// A dedicated thread owns its connection. Synchronous SQLite never runs on an Avalonia thread.
internal sealed class SqliteLane : IAsyncDisposable
{
    private readonly Channel<IWork> _interactive = Channel.CreateBounded<IWork>(16);
    private readonly Channel<IWork> _background = Channel.CreateBounded<IWork>(8);
    private readonly SemaphoreSlim _signal = new(0);
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _waitingAdmissions;
    private volatile bool _closed;

    public SqliteLane(string path, bool readOnly, Action<SqliteConnection>? initialize = null)
    {
        var thread = new Thread(() => Run(path, readOnly, initialize)) { IsBackground = true, Name = "echo-sqlite" };
        thread.Start();
    }

    public Task Ready => _ready.Task;

    public async Task<T> QueueAsync<T>(Func<SqliteConnection, T> action, bool interactive,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Interlocked.Increment(ref _waitingAdmissions) > 32)
        {
            Interlocked.Decrement(ref _waitingAdmissions);
            throw new LibraryBusyException();
        }
        Work<T>? work = null;
        try
        {
            await _ready.Task.WaitAsync(cancellation).ConfigureAwait(false);
            if (_closed) throw new ObjectDisposedException(nameof(SqliteLane));
            work = new(action, cancellation);
            await (interactive ? _interactive : _background).Writer.WriteAsync(work, cancellation).ConfigureAwait(false);
            _signal.Release();
        }
        catch { work?.Reject(); throw; }
        finally { Interlocked.Decrement(ref _waitingAdmissions); }
        return await work.Result.ConfigureAwait(false);
    }

    private void Run(string path, bool readOnly, Action<SqliteConnection>? initialize)
    {
        try
        {
            var settings = new SqliteConnectionStringBuilder
            {
                DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
                Pooling = false, DefaultTimeout = 1
            };
            using var connection = new SqliteConnection(settings.ToString());
            connection.Open();
            connection.CreateFunction<string, string>("echo_fold", Echo.Infrastructure.Search.LibrarySearchSchema.Fold, isDeterministic: true);
            connection.CreateFunction<string, string?>("echo_directory", value => Path.GetDirectoryName(value), isDeterministic: true);
            connection.CreateFunction<string, string>("echo_folder_prefix", Echo.Infrastructure.Files.LibraryPath.RootPrefix, isDeterministic: true);
            connection.CreateFunction<string, string>("echo_genre_key", GenreSchema.Key, isDeterministic: true);
            connection.CreateFunction<string, string>("echo_genre_name", GenreSchema.Name, isDeterministic: true);
            initialize?.Invoke(connection);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = readOnly
                    ? "PRAGMA query_only=ON; PRAGMA cache_size=-4096; PRAGMA busy_timeout=1000;"
                    : "PRAGMA cache_size=-8192; PRAGMA busy_timeout=1000; PRAGMA foreign_keys=ON; PRAGMA temp_store=FILE;";
                command.ExecuteNonQuery();
            }
            _ready.TrySetResult();
            var interactiveBurst = 0;
            while (true)
            {
                _signal.Wait();
                IWork? work;
                if (interactiveBurst >= 8 && _background.Reader.TryRead(out work)) interactiveBurst = 0;
                else if (_interactive.Reader.TryRead(out work)) interactiveBurst++;
                else if (_background.Reader.TryRead(out work)) interactiveBurst = 0;
                else
                {
                    if (_closed) break;
                    continue;
                }
                work.Execute(connection);
            }
            connection.Close();
            _stopped.TrySetResult();
        }
        catch (Exception error)
        {
            _closed = true;
            _ready.TrySetException(error);
            _interactive.Writer.TryComplete(error);
            _background.Writer.TryComplete(error);
            while (_interactive.Reader.TryRead(out var work)) work.Fail(error);
            while (_background.Reader.TryRead(out var work)) work.Fail(error);
            _stopped.TrySetException(error);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _closed = true;
        _interactive.Writer.TryComplete();
        _background.Writer.TryComplete();
        _signal.Release();
        try { await _stopped.Task.ConfigureAwait(false); }
        finally
        {
            // An admitted producer may still be publishing its wake-up after shutdown starts.
            while (Volatile.Read(ref _waitingAdmissions) != 0) await Task.Delay(1).ConfigureAwait(false);
            _signal.Dispose();
        }
    }

    private interface IWork
    {
        void Execute(SqliteConnection connection);
        void Fail(Exception error);
    }

    private sealed class Work<T> : IWork
    {
        private readonly Func<SqliteConnection, T> _action;
        private readonly TaskCompletionSource<T> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _cancellation;
        private CancellationTokenRegistration _registration;
        private int _state;

        public Work(Func<SqliteConnection, T> action, CancellationToken cancellation)
        {
            _action = action;
            _cancellation = cancellation;
            _registration = cancellation.Register(() =>
            {
                // Once execution starts, the operation owns its commit/rollback outcome.
                if (Interlocked.CompareExchange(ref _state, 2, 0) == 0) _result.TrySetCanceled(cancellation);
            });
        }
        public Task<T> Result => _result.Task;
        public void Execute(SqliteConnection connection)
        {
            try
            {
                if (Interlocked.CompareExchange(ref _state, 1, 0) != 0) return;
                _cancellation.ThrowIfCancellationRequested();
                _result.TrySetResult(_action(connection));
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            { _result.TrySetCanceled(_cancellation); }
            catch (SqliteException) when (_cancellation.IsCancellationRequested)
            { _result.TrySetCanceled(_cancellation); }
            catch (Exception error) { _result.TrySetException(error); }
            finally { _registration.Dispose(); }
        }
        public void Reject() { _registration.Dispose(); }
        public void Fail(Exception error) { _registration.Dispose(); _result.TrySetException(error); }
    }
}
