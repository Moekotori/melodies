using System.IO.Pipes;
using System.Text.Json;
using Echo.AlbumInformation;
using Echo.Core.Contracts;

// A bounded metadata capability in the staged Core migration; no window or database ownership.
var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "core");
Directory.CreateDirectory(root);
using var instance = TryOwn(Path.Combine(root, "metadata-owner.lock"));
if (instance is null) return;
using var service = new AlbumInformationService(Path.Combine(root, "album-information"));
using var shutdown = new CancellationTokenSource();
using var clients = new SemaphoreSlim(4, 4);
var generation = Guid.NewGuid().ToString("N");
long revision = 0, activity = Environment.TickCount64;
var active = 0;
var sessions = new List<Task>(4);
var idle = Task.Run(async () =>
{
    while (!shutdown.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), shutdown.Token).ConfigureAwait(false);
        if (Volatile.Read(ref active) == 0 && Environment.TickCount64 - Interlocked.Read(ref activity) > 180000) shutdown.Cancel();
    }
});
try
{
    while (!shutdown.IsCancellationRequested)
    {
        await clients.WaitAsync(shutdown.Token).ConfigureAwait(false);
        NamedPipeServerStream? pipe = null;
        try
        {
            pipe = new(CoreMetadataProtocol.PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 8192, 8192);
            await pipe.WaitForConnectionAsync(shutdown.Token).ConfigureAwait(false);
            Interlocked.Increment(ref active); Interlocked.Exchange(ref activity, Environment.TickCount64);
            var connected = pipe; pipe = null;
            sessions.RemoveAll(task => task.IsCompleted);
            sessions.Add(HandleAsync(connected));
        }
        catch { pipe?.Dispose(); clients.Release(); throw; }
    }
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
finally
{
    shutdown.Cancel();
    await Task.WhenAll(sessions).ConfigureAwait(false);
    try { await idle.ConfigureAwait(false); } catch (OperationCanceledException) { }
}
async Task HandleAsync(NamedPipeServerStream connection)
{
    await using var pipe = connection;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(80));
    try
    {
        var bytes = await CoreMetadataProtocol.ReadAsync(pipe, deadline.Token).ConfigureAwait(false);
        var request = JsonSerializer.Deserialize(bytes, CoreMetadataJson.Default.CoreMetadataRequest) ?? throw new IOException("Invalid request.");
        if (request.Version != CoreMetadataProtocol.Version || !Guid.TryParse(request.RequestId, out _)) throw new IOException("Unsupported protocol.");
        AlbumInformationResult? result = null; string? error = null;
        try { result = await service.ReadAsync(request.Information, deadline.Token).ConfigureAwait(false); }
        catch (ArgumentException) { error = "专辑名称或艺术家信息无效。"; }
        catch (Exception) { error = "资料服务暂时不可用，请重试。"; }
        var response = new CoreMetadataResponse(CoreMetadataProtocol.Version, request.RequestId, CoreMetadataProtocol.CoreId,
            generation, Interlocked.Increment(ref revision), result, error);
        await CoreMetadataProtocol.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(response, CoreMetadataJson.Default.CoreMetadataResponse), deadline.Token).ConfigureAwait(false);
    }
    catch (Exception error) when (error is IOException or OperationCanceledException or JsonException) { }
    finally { Interlocked.Decrement(ref active); Interlocked.Exchange(ref activity, Environment.TickCount64); clients.Release(); }
}
static FileStream? TryOwn(string path)
{
    try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    catch (IOException) { return null; }
}
