using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback.NativeAudio;

/// <summary>Only owns the process it starts. PCM never enters the managed process.</summary>
public sealed partial class NativeAudioHost(string executable, bool allowProDsp = false) : INativeAudio
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writes = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private TaskCompletionSource<JsonElement> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Process? process;
    private Task? readerTask, stderrTask;
    private long nextId;
    private int started, disposed;
    public event Action<string, JsonElement>? Notification;
    public event Action<Exception>? Failed;
    public JsonElement? Capabilities { get; private set; }
    public int? ProcessId => process is { HasExited: false } ? process.Id : null;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (Interlocked.Exchange(ref started, 1) == 0)
        {
            try
            {
                if (process is not null)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                    foreach (var task in new[] { readerTask, stderrTask })
                        if (task is not null) try { await task.ConfigureAwait(false); } catch (Exception) { }
                    process.Dispose();
                }
                ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
                bootstrapReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Interlocked.Exchange(ref started, 1);
                var path = Path.GetFullPath(executable);
                if (!File.Exists(path)) throw new FileNotFoundException("找不到 Native Audio Host，先运行导入脚本", path);
                var start = new ProcessStartInfo(path)
                {
                    WorkingDirectory = Path.GetDirectoryName(path)!, UseShellExecute = false,
                    CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                    RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false),
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                foreach (var arg in new[] { "--no-stdin", "--defer-device-open", "--rpc-stdin-fd", "0", "--rpc-stdout-fd", "1" })
                    start.ArgumentList.Add(arg);
                if (allowProDsp) start.ArgumentList.Add("--allow-pro-dsp");
                if (OutputChannels > 2) start.ArgumentList.Add("--defer-channel-init");
                process = Process.Start(start) ?? throw new IOException("无法启动音频宿主");
                readerTask = ReadOutputAsync();
                // Drain without retaining paths, credentials or unbounded logs.
                stderrTask = BoundedJsonLines.ReadAsync(process.StandardError, _ => { }, lifetime.Token);
                if (OutputChannels > 2)
                {
                    await bootstrapReady.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
                    var initialized = await CallAsync("host.initialize", PlaybackSessionCoordinator.Params(new JsonObject { ["channels"] = OutputChannels }), cancellationToken).ConfigureAwait(false);
                    if (!initialized.Flag("accepted") || initialized.Long("channels") != OutputChannels) throw new IOException("原生声道初始化未确认");
                }
            }
            catch (Exception e) { Break(e); throw; }
        }
        var r = await ready.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
        if (r.Long("protocolVersion") != 1 || r.Long("backendContractVersion") != 2
            || r.Child("capabilities") is not { } caps || !caps.Flag("hostOwnedLocalPlaybackV1"))
            throw new IOException("Native Audio Host 协议/播放能力不匹配");
        Capabilities = r.Child("capabilities");
    }

    private async Task ReadOutputAsync()
    {
        try
        {
            await BoundedJsonLines.ReadAsync(process!.StandardOutput, Receive, lifetime.Token).ConfigureAwait(false);
            if (disposed == 0 && !restarting) Break(new IOException("Native Audio Host 已退出"));
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (disposed == 0 && !restarting) Break(e); }
    }

    private void Receive(string line)
    {
        using var document = JsonDocument.Parse(line);
        var message = document.RootElement;
        if (message.Flag("ready"))
        { if (message.Text("readyLevel") == "bootstrap") bootstrapReady.TrySetResult(); else ready.TrySetResult(message.Clone()); return; }
        if (message.TryGetProperty("id", out var id) && id.TryGetInt64(out var n) && pending.TryRemove(n, out var completion))
        {
            if (message.TryGetProperty("error", out var error))
                completion.TrySetException(new IOException($"Native RPC 错误 {error.Long("code")}: {error.Text("message")}"));
            else if (message.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
            else completion.TrySetException(new IOException("无效的 Native RPC 响应"));
        }
        else if (message.Text("method") is { } method)
            Notification?.Invoke(method, message.Child("params") ?? default);
    }

    public async Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (process is null || process.HasExited) throw new IOException("音频宿主未运行");
        if (pending.Count >= 64) throw new IOException("Native RPC 待处理请求已满");
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject(); writer.WriteString("jsonrpc", "2.0"); writer.WriteNumber("id", id);
                writer.WriteString("method", method);
                if (parameters is { } p) { writer.WritePropertyName("params"); p.WriteTo(writer); }
                writer.WriteEndObject();
            }
            if (bytes.Length > 256 * 1024) throw new ArgumentException("Native RPC 请求超过 256 KiB 上限");
            var line = Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
            await writes.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { writes.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        finally { pending.TryRemove(id, out _); }
    }

    private void Break(Exception error)
    {
        Interlocked.Exchange(ref started, 0);
        ready.TrySetException(error);
        bootstrapReady.TrySetException(error);
        foreach (var item in pending) if (pending.TryRemove(item.Key, out var c)) c.TrySetException(error);
        Failed?.Invoke(error);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed != 0) return;
        if (process is { HasExited: false })
        {
            try { await CallAsync("rpc.shutdown").WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (Exception) { }
        }
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        Break(new ObjectDisposedException(nameof(NativeAudioHost)));
        if (process is { HasExited: false })
        {
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); }
        }
        foreach (var task in new[] { readerTask, stderrTask })
            if (task is not null) try { await task.ConfigureAwait(false); } catch (OperationCanceledException) { }
        process?.Dispose(); lifetime.Dispose(); writes.Dispose();
    }
}
