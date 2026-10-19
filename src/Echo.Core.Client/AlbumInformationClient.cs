using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Echo.Core.Contracts;

namespace Echo.Core.Client;

public sealed class AlbumInformationClient(string? hostDirectory = null) : IAlbumInformationClient
{
    private static readonly SemaphoreSlim Startup = new(1, 1);
    private int _pending;
    public async Task<AlbumInformationResult> ReadAsync(AlbumInformationRequest request, CancellationToken cancellation = default)
    {
        if (Interlocked.Increment(ref _pending) > 8) { Interlocked.Decrement(ref _pending); throw new IOException("资料请求较多，请稍后重试。"); }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(85));
            await using var pipe = await ConnectAsync(deadline.Token).ConfigureAwait(false);
            var id = Guid.NewGuid().ToString("N");
            var envelope = new CoreMetadataRequest(CoreMetadataProtocol.Version, id, request);
            await CoreMetadataProtocol.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(envelope, CoreMetadataJson.Default.CoreMetadataRequest), deadline.Token).ConfigureAwait(false);
            var bytes = await CoreMetadataProtocol.ReadAsync(pipe, deadline.Token).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize(bytes, CoreMetadataJson.Default.CoreMetadataResponse) ?? throw new IOException("资料服务返回无效结果。");
            if (response.Version != CoreMetadataProtocol.Version || response.RequestId != id || response.CoreId != CoreMetadataProtocol.CoreId || !Guid.TryParse(response.Generation, out _))
                throw new IOException("资料服务连接已变化，请重新请求。");
            return response.Information ?? throw new IOException(response.Error ?? "资料服务没有返回结果。");
        }
        finally { Interlocked.Decrement(ref _pending); }
    }
    private async Task<NamedPipeClientStream> ConnectAsync(CancellationToken token)
    {
        var connection = await TryConnectAsync(250, token).ConfigureAwait(false);
        if (connection is not null) return connection;
        await Startup.WaitAsync(token).ConfigureAwait(false);
        try
        {
            connection = await TryConnectAsync(250, token).ConfigureAwait(false);
            if (connection is not null) return connection;
            var root = hostDirectory ?? Path.Combine(AppContext.BaseDirectory, "core");
            var binary = Path.Combine(root, OperatingSystem.IsWindows() ? "Echo.Core.Host.exe" : "Echo.Core.Host");
            ProcessStartInfo start;
            if (File.Exists(binary)) start = new(binary);
            else if (File.Exists(Path.Combine(root, "Echo.Core.Host.dll")))
            { start = new("dotnet"); start.ArgumentList.Add(Path.Combine(root, "Echo.Core.Host.dll")); }
            else throw new IOException("专辑资料服务缺失，请重新构建或安装完整应用。");
            start.UseShellExecute = false; start.CreateNoWindow = true;
            using var process = Process.Start(start) ?? throw new IOException("无法启动专辑资料服务。");
            return await TryConnectAsync(6000, token).ConfigureAwait(false) ?? throw new IOException("无法连接专辑资料服务，请重试。");
        }
        finally { Startup.Release(); }
    }
    private static async Task<NamedPipeClientStream?> TryConnectAsync(int timeout, CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", CoreMetadataProtocol.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(timeout, token).ConfigureAwait(false); return pipe; }
        catch (TimeoutException) { pipe.Dispose(); return null; }
        catch { pipe.Dispose(); throw; }
    }
}
