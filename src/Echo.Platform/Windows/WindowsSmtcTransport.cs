using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Echo.Playback.MediaTransport;

namespace Echo.Platform.Windows;

/// <summary>Owns one SMTC helper. Latest state only; no process or file I/O on the UI thread.</summary>
public sealed class WindowsSmtcTransport : IMediaTransport
{
    private readonly string binary;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task worker;
    private MediaTransportState? latest;
    private Process? host;
    private Task[] readers = [];
    private TaskCompletionSource? ready;
    private MediaTransportState? published;
    private bool unavailable;
    private Exception? readFailure;
    private readonly object diagnosticGate = new();
    private bool diagnosticPublished;
    private string? lastDiagnostic;
    private int retry, disposed;
    public event Action<MediaTransportCommand>? Command;
    public event Action<string?>? DiagnosticChanged;

    public WindowsSmtcTransport(string binary)
    {
        this.binary = Path.GetFullPath(binary);
        worker = Task.Run(RunAsync);
    }

    public void Publish(MediaTransportState state)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Volatile.Write(ref latest, state);
        wake.Writer.TryWrite(true);
    }
    public void Retry()
    {
        if (Volatile.Read(ref disposed) != 0) return;
        Interlocked.Exchange(ref retry, 1);
        wake.Writer.TryWrite(true);
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var _ in wake.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
            {
                try
                {
                    if (Interlocked.Exchange(ref retry, 0) != 0)
                    {
                        await StopHostAsync().ConfigureAwait(false);
                        unavailable = false;
                    }
                    var state = Volatile.Read(ref latest);
                    if (state is null || unavailable || host is null && state.ItemId is null) continue;
                    await EnsureHostAsync().ConfigureAwait(false);
                    // Coalesce clock ticks; important state changes and seeks publish immediately.
                    var started = Stopwatch.GetTimestamp();
                    foreach (var message in SmtcProtocol.Updates(state, published))
                        await WriteAsync(message, lifetime.Token).ConfigureAwait(false);
                    published = state;
                    var elapsed = Stopwatch.GetElapsedTime(started);
                    if (elapsed < TimeSpan.FromMilliseconds(100))
                        await Task.Delay(TimeSpan.FromMilliseconds(100) - elapsed, lifetime.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    unavailable = true;
                    Report("系统媒体控制暂不可用。可点击重试，播放仍可继续。");
                    await StopHostAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { await StopHostAsync().ConfigureAwait(false); }
    }

    private async Task EnsureHostAsync()
    {
        if (host is not null)
        {
            if (Volatile.Read(ref readFailure) is { } failure) throw new IOException("SMTC pipe failed", failure);
            if (host.HasExited) throw new IOException("SMTC host exited");
            return;
        }
        if (!File.Exists(binary)) throw new FileNotFoundException("SMTC host missing");
        var start = new ProcessStartInfo(binary)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        start.ArgumentList.Add("--aumid=com.echo.avalonia");
        host = new Process { StartInfo = start };
        ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!host.Start()) throw new IOException("SMTC host failed to start");
        published = null;
        Volatile.Write(ref readFailure, null);
        readers = [ReadAsync(host.StandardOutput, true), ReadAsync(host.StandardError, false)];
        await ready.Task.WaitAsync(TimeSpan.FromMilliseconds(2500), lifetime.Token).ConfigureAwait(false);
        Report(null);
    }

    private async Task ReadAsync(StreamReader reader, bool protocol)
    {
        try
        {
            var buffer = new char[2048];
            var line = new StringBuilder(2048);
            while (!lifetime.IsCancellationRequested)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), lifetime.Token).ConfigureAwait(false);
                if (count == 0) break;
                foreach (var character in buffer.AsSpan(0, count))
                {
                    if (character == '\n')
                    {
                        if (protocol && line.Length > 0) Receive(line.ToString());
                        line.Clear();
                    }
                    else
                    {
                        if (line.Length >= 16 * 1024) throw new IOException("SMTC line too large");
                        line.Append(character);
                    }
                }
            }
            var failure = new IOException("SMTC pipe closed");
            Volatile.Write(ref readFailure, failure);
            ready?.TrySetException(failure);
            // Wake even when playback has paused and no clock updates are arriving.
            wake.Writer.TryWrite(true);
        }
        catch (Exception e)
        {
            Volatile.Write(ref readFailure, e);
            ready?.TrySetException(e);
            if (!lifetime.IsCancellationRequested) { Report("系统媒体控制连接已中断。可点击重试。"); wake.Writer.TryWrite(true); }
        }
    }

    private void Receive(string line)
    {
        using var document = JsonDocument.Parse(line);
        var message = document.RootElement;
        if (SmtcProtocol.Text(message, "type") == "ready")
        {
            if (SmtcProtocol.Ready(message)) ready?.TrySetResult();
            else ready?.TrySetException(new IOException("SMTC protocol mismatch"));
        }
        else if (SmtcProtocol.Command(message) is { } command) Command?.Invoke(command);
        else if (SmtcProtocol.Text(message, "type") == "error") Report("系统媒体控制更新失败。可点击重试。");
    }

    private async Task WriteAsync(string line, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await host!.StandardInput.WriteLineAsync(line.AsMemory(), timeout.Token).ConfigureAwait(false);
        await host.StandardInput.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private async Task StopHostAsync()
    {
        var process = host;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                using var timeout = new CancellationTokenSource(500);
                try
                {
                    await WriteAsync("{\"type\":\"dispose\"}", timeout.Token).ConfigureAwait(false);
                    process.StandardInput.Close();
                    await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (Exception) { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            }
            try { await Task.WhenAll(readers).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); } catch (Exception) { }
        }
        finally { host = null; readers = []; published = null; ready = null; process.Dispose(); }
    }
    private void Report(string? message)
    {
        lock (diagnosticGate)
        {
            if (diagnosticPublished && lastDiagnostic == message) return;
            diagnosticPublished = true;
            lastDiagnostic = message;
        }
        try { DiagnosticChanged?.Invoke(message); } catch (Exception) { /* Optional UI diagnostics. */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        wake.Writer.TryComplete();
        await lifetime.CancelAsync().ConfigureAwait(false);
        await worker.ConfigureAwait(false);
        Command = null; DiagnosticChanged = null;
        lifetime.Dispose();
    }
}
