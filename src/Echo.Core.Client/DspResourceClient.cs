using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using Echo.Core.Contracts;

namespace Echo.Core.Client;

public sealed class DspResourceClient(string? hostDirectory = null) : IDspResourceClient
{
    private static readonly SemaphoreSlim Startup = new(1, 1);
    private int pending;
    public async Task<JsonElement> ExecuteAsync(DspResourceOperation operation, string? id = null, string? name = null,
        string? path = null, string? query = null, int offset = 0, JsonElement? state = null, bool refresh = false, CancellationToken token = default)
    {
        if (Interlocked.Increment(ref pending) > 8) { Interlocked.Decrement(ref pending); throw new IOException("DSP 资源请求较多，请稍后重试。"); }
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(85));
            await using var pipe = await ConnectAsync(deadline.Token).ConfigureAwait(false);
            var request = new DspResourceRequest(CoreDspProtocol.Version, Guid.NewGuid().ToString("N"), operation, id, name, path, query, offset, state, refresh);
            await CoreMetadataProtocol.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request, CoreDspJson.Default.DspResourceRequest), deadline.Token).ConfigureAwait(false);
            var response = JsonSerializer.Deserialize(await CoreMetadataProtocol.ReadAsync(pipe, deadline.Token).ConfigureAwait(false), CoreDspJson.Default.DspResourceResponse)
                ?? throw new IOException("DSP 资源服务没有返回结果。");
            if (response.Version != CoreDspProtocol.Version || response.RequestId != request.RequestId || response.CoreId != CoreMetadataProtocol.CoreId || !Guid.TryParse(response.Generation, out _)) throw new IOException("DSP 资源服务连接已变化。");
            return response.Result ?? throw new IOException(response.Error ?? "DSP 资源操作失败。");
        }
        finally { Interlocked.Decrement(ref pending); }
    }
    private async Task<NamedPipeClientStream> ConnectAsync(CancellationToken token)
    {
        var pipe = await TryConnectAsync(250, token).ConfigureAwait(false); if (pipe is not null) return pipe;
        await Startup.WaitAsync(token).ConfigureAwait(false);
        try
        {
            pipe = await TryConnectAsync(250, token).ConfigureAwait(false); if (pipe is not null) return pipe;
            var root = hostDirectory ?? Path.Combine(AppContext.BaseDirectory, "core");
            var binary = Path.Combine(root, OperatingSystem.IsWindows() ? "Echo.Core.Host.exe" : "Echo.Core.Host");
            var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
            if (File.Exists(binary)) start.FileName = binary;
            else if (File.Exists(Path.Combine(root, "Echo.Core.Host.dll"))) { start.FileName = "dotnet"; start.ArgumentList.Add(Path.Combine(root, "Echo.Core.Host.dll")); }
            else throw new IOException("DSP 资源服务缺失，请重新构建完整应用。");
            using var process = Process.Start(start) ?? throw new IOException("无法启动 DSP 资源服务。");
            return await TryConnectAsync(6000, token).ConfigureAwait(false) ?? throw new IOException("无法连接 DSP 资源服务；旧 Core 可能尚未退出，请重启服务后重试。");
        }
        finally { Startup.Release(); }
    }
    private static async Task<NamedPipeClientStream?> TryConnectAsync(int milliseconds, CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", CoreDspProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(milliseconds, token).ConfigureAwait(false); return pipe; }
        catch (TimeoutException) { pipe.Dispose(); return null; } catch { pipe.Dispose(); throw; }
    }
}
