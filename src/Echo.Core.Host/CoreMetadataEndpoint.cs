using System.IO.Pipes;
using System.Text.Json;
using Echo.AlbumInformation;
using Echo.Core.Contracts;

// Compatibility capability served by the same Core owner, never a second host.
internal sealed class CoreMetadataEndpoint(string root) : IDisposable
{
    private readonly AlbumInformationService service = new(Path.Combine(root, "album-information"));
    private readonly string generation = Guid.NewGuid().ToString("N");
    public string Generation => generation;
    private long revision, lastActivity;
    private int active;
    public bool HasActivity => Volatile.Read(ref active) > 0 ||
        (Interlocked.Read(ref lastActivity) is var last && last != 0 && Environment.TickCount64 - last < 180000);

    public async Task RunAsync(CancellationToken shutdown)
    {
        using var slots = new SemaphoreSlim(4, 4);
        var sessions = new List<Task>(4);
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                await slots.WaitAsync(shutdown).ConfigureAwait(false);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new(CoreMetadataProtocol.PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 8192, 8192);
                    await pipe.WaitForConnectionAsync(shutdown).ConfigureAwait(false);
                    Interlocked.Increment(ref active); Interlocked.Exchange(ref lastActivity, Environment.TickCount64);
                    var connected = pipe; pipe = null;
                    sessions.RemoveAll(task => task.IsCompleted);
                    sessions.Add(HandleAsync(connected, slots, shutdown));
                }
                catch { pipe?.Dispose(); slots.Release(); throw; }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally { await Task.WhenAll(sessions).ConfigureAwait(false); }
    }

    private async Task HandleAsync(NamedPipeServerStream connection, SemaphoreSlim slots, CancellationToken shutdown)
    {
        await using var pipe = connection;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        deadline.CancelAfter(TimeSpan.FromSeconds(80));
        try
        {
            var bytes = await CoreMetadataProtocol.ReadAsync(pipe, deadline.Token).ConfigureAwait(false);
            var request = JsonSerializer.Deserialize(bytes, CoreMetadataJson.Default.CoreMetadataRequest)
                ?? throw new IOException("Invalid request.");
            if (request.Version != CoreMetadataProtocol.Version || !Guid.TryParse(request.RequestId, out _))
                throw new IOException("Unsupported protocol.");
            AlbumInformationResult? result = null; string? error = null;
            try { result = await service.ReadAsync(request.Information, deadline.Token).ConfigureAwait(false); }
            catch (ArgumentException) { error = "专辑名称或艺术家信息无效。"; }
            catch (Exception) { error = "资料服务暂时不可用，请重试。"; }
            var response = new CoreMetadataResponse(CoreMetadataProtocol.Version, request.RequestId, CoreMetadataProtocol.CoreId,
                generation, Interlocked.Increment(ref revision), result, error);
            await CoreMetadataProtocol.WriteAsync(pipe,
                JsonSerializer.SerializeToUtf8Bytes(response, CoreMetadataJson.Default.CoreMetadataResponse), deadline.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or JsonException) { }
        finally { Interlocked.Decrement(ref active); Interlocked.Exchange(ref lastActivity, Environment.TickCount64); slots.Release(); }
    }
    public void Dispose() => service.Dispose();
}
