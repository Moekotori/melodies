using System.IO.Pipes;
using System.Text.Json;
using Echo.Core.Contracts;
using Echo.Dsp;

internal sealed class DspResourceEndpoint(DspResourceService service, string generation, string? pipeName = null)
{
    public int ActiveRequests;
    public long LastActivity = Environment.TickCount64;
    public async Task RunAsync(CancellationToken shutdown)
    {
        using var slots = new SemaphoreSlim(4, 4); var sessions = new List<Task>(4);
        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                await slots.WaitAsync(shutdown).ConfigureAwait(false);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new(pipeName ?? CoreDspProtocol.PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 8192, 8192);
                    await pipe.WaitForConnectionAsync(shutdown).ConfigureAwait(false);
                    Interlocked.Increment(ref ActiveRequests); Interlocked.Exchange(ref LastActivity, Environment.TickCount64);
                    var connection = pipe; pipe = null; sessions.RemoveAll(x => x.IsCompleted); sessions.Add(HandleAsync(connection, slots, shutdown));
                }
                catch { pipe?.Dispose(); slots.Release(); throw; }
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested) { }
        finally { await Task.WhenAll(sessions).ConfigureAwait(false); }
    }
    private async Task HandleAsync(NamedPipeServerStream connection, SemaphoreSlim slots, CancellationToken shutdown)
    {
        await using var pipe = connection; using var timeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown); timeout.CancelAfter(TimeSpan.FromSeconds(80));
        try
        {
            var bytes = await CoreMetadataProtocol.ReadAsync(pipe, timeout.Token).ConfigureAwait(false);
            var request = JsonSerializer.Deserialize(bytes, CoreDspJson.Default.DspResourceRequest) ?? throw new IOException("Invalid DSP resource request.");
            if (request.Version != CoreDspProtocol.Version || !Guid.TryParse(request.RequestId, out _) || !Enum.IsDefined(request.Operation)) throw new IOException("Unsupported DSP resource protocol.");
            JsonElement? result = null; string? error = null;
            try { result = await service.ExecuteAsync(request, timeout.Token).ConfigureAwait(false); }
            catch (Exception failure) when (failure is IOException or ArgumentException or JsonException or HttpRequestException or System.Data.Common.DbException or UnauthorizedAccessException or FormatException or OverflowException or InvalidOperationException) { error = failure.Message; }
            var response = new DspResourceResponse(CoreDspProtocol.Version, request.RequestId, CoreMetadataProtocol.CoreId, generation, service.Revision, result, error);
            await CoreMetadataProtocol.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(response, CoreDspJson.Default.DspResourceResponse), timeout.Token).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is IOException or OperationCanceledException or JsonException) { }
        finally { Interlocked.Decrement(ref ActiveRequests); Interlocked.Exchange(ref LastActivity, Environment.TickCount64); slots.Release(); }
    }
}
