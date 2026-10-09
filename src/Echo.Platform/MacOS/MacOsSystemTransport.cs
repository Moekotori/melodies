using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Echo.Playback.MediaTransport;

namespace Echo.Platform.MacOS;

/// <summary>One macOS system host. Latest Now Playing state only; playback stays in the session.</summary>
public sealed partial class MacOsSystemTransport : IMediaTransport, IMediaFileActivation, IMediaAccessory
{
    private readonly string binary;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Channel<bool> wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly object io = new();
    private readonly Task worker;
    private MediaTransportState? latest, published;
    private Process? host;
    private Task? reader;
    private TaskCompletionSource<bool>? pending;
    private string? pendingId;
    private int sequence, retry, disposed;
    private bool unavailable;
    public event Action<MediaTransportCommand>? Command;
    public event Action<string?>? DiagnosticChanged;
    public event Action<IReadOnlyList<string>>? FilesOpened;

    public MacOsSystemTransport(string binary)
    {
        this.binary = Path.GetFullPath(binary);
        worker = Task.Run(RunAsync);
    }

    public static string ResolveBinary()
    {
        var configured = Environment.GetEnvironmentVariable("ECHO_MACOS_SYSTEM_HOST");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(AppContext.BaseDirectory, "native", "echo-macos-system-host")
            : configured;
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
                    var publish = state is not null && !unavailable && !(host is null && state.ItemId is null) && Changed(state, published);
                    var accessory = !unavailable && HasAccessoryWork();
                    if (!publish && !accessory) continue;
                    await EnsureHostAsync().ConfigureAwait(false);
                    if (publish && state is not null)
                    {
                        if (state.ItemId is null) await RequestAsync("now-playing.clear", null).ConfigureAwait(false);
                        else
                        {
                            if (state.FilePath is { Length: > 0 } file && file != published?.FilePath)
                            {
                                try { await RequestAsync("current-file.set", writer => writer.WriteString("filePath", file)).ConfigureAwait(false); }
                                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
                                catch (Exception) { }
                            }
                            await RequestAsync("now-playing.update", writer => WritePayload(writer, state)).ConfigureAwait(false);
                        }
                        published = state;
                    }
                    await SyncAccessoriesAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { break; }
                catch (Exception)
                {
                    unavailable = true;
                    DiagnosticChanged?.Invoke("macOS 系统媒体暂不可用。可点击重试，播放仍可继续。");
                    await StopHostAsync().ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally { await StopHostAsync().ConfigureAwait(false); }
    }

    private static bool Changed(MediaTransportState state, MediaTransportState? previous)
    {
        if (previous is null || state.ItemId != previous.ItemId || state.Status != previous.Status || state.Title != previous.Title
            || state.Artist != previous.Artist || state.Album != previous.Album || state.CoverPath != previous.CoverPath
            || state.FilePath != previous.FilePath || state.OutputName != previous.OutputName) return true;
        return Math.Abs(state.PositionSeconds - previous.PositionSeconds) >= 5;
    }

    private async Task EnsureHostAsync()
    {
        if (host is not null)
        {
            if (host.HasExited) throw new IOException("macOS system host exited");
            return;
        }
        if (!File.Exists(binary)) throw new FileNotFoundException("macOS system host missing", binary);
        var start = new ProcessStartInfo(binary)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8
        };
        host = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!host.Start()) throw new IOException("macOS system host failed to start");
        published = null;
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = ready; pendingId = "";
        reader = Task.Run(() => ReadAsync(ready));
        _ = host.StandardError.ReadToEndAsync(lifetime.Token);
        if (!await ready.Task.WaitAsync(TimeSpan.FromSeconds(8), lifetime.Token).ConfigureAwait(false))
            throw new IOException("macOS system host was not ready");
        DiagnosticChanged?.Invoke(null);
    }

    private async Task ReadAsync(TaskCompletionSource<bool> ready)
    {
        try
        {
            while (host?.StandardOutput.ReadLine() is { } line)
            {
                if (line.Length == 0 || line.Length > 65536) continue;
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var type = Text(root, "type");
                if (type == "ready") { ready.TrySetResult(true); continue; }
                if (type == "extension-request") { ReplyExtension(Text(root, "requestId")); continue; }
                if (type == "response")
                {
                    var id = Text(root, "requestId");
                    if (id == pendingId) pending?.TrySetResult(root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True);
                    continue;
                }
                if (type == "event" && Text(root, "event") == "output-disconnect")
                {
                    if (PauseOnOutputDisconnect) Command?.Invoke(new("pause"));
                    continue;
                }
                if (type == "command") HandleCommand(root);
            }
        }
        catch (Exception) { ready.TrySetResult(false); pending?.TrySetResult(false); }
    }

    private void HandleCommand(JsonElement root)
    {
        var name = Text(root, "command");
        if (name == "open-files")
        {
            if (!root.TryGetProperty("paths", out var paths) || paths.ValueKind != JsonValueKind.Array) return;
            var files = new List<string>(8);
            foreach (var path in paths.EnumerateArray())
            {
                if (path.ValueKind != JsonValueKind.String || path.GetString() is not { Length: > 0 and <= 1024 } value || value.Contains('\0')) continue;
                files.Add(value);
                if (files.Count == 64) break;
            }
            if (files.Count > 0) FilesOpened?.Invoke(files);
            return;
        }
        if (name == "toggle") name = published?.Status is "playing" or "loading" ? "pause" : "play";
        if (name is "play" or "pause" or "stop" or "next" or "previous") { Command?.Invoke(new(name)); return; }
        if (name == "seek" && root.TryGetProperty("positionSeconds", out var position) && position.TryGetDouble(out var seconds)
            && double.IsFinite(seconds) && seconds is >= 0 and <= 31_536_000) Command?.Invoke(new(name, seconds));
    }

    private void ReplyExtension(string? id)
    {
        if (string.IsNullOrEmpty(id)) return;
        var request = NextId();
        Write($"{{\"type\":\"extension.reply\",\"requestId\":\"{request}\",\"extensionRequestId\":\"{id}\",\"response\":{{\"ok\":false,\"error\":\"busy\"}}}}");
    }

    private async Task RequestAsync(string type, Action<Utf8JsonWriter>? body)
    {
        var id = NextId();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", type);
            writer.WriteString("requestId", id);
            body?.Invoke(writer);
            writer.WriteEndObject();
        }
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending = completion; pendingId = id;
        Write(Encoding.UTF8.GetString(stream.ToArray()));
        if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(8), lifetime.Token).ConfigureAwait(false))
            throw new IOException("macOS system host rejected " + type);
    }

    private static void WritePayload(Utf8JsonWriter writer, MediaTransportState state)
    {
        writer.WriteStartObject("payload");
        writer.WriteString("title", string.IsNullOrWhiteSpace(state.Title) ? "ECHO" : state.Title);
        WriteOptional(writer, "artist", state.Artist);
        WriteOptional(writer, "album", state.Album);
        WriteOptional(writer, "outputName", state.OutputName);
        WriteOptional(writer, "artworkPath", state.CoverPath);
        WriteOptional(writer, "filePath", state.FilePath);
        writer.WriteString("state", state.Status is "playing" or "loading" ? "playing" : state.Status == "paused" ? "paused" : "stopped");
        writer.WriteNumber("positionSeconds", state.PositionSeconds);
        writer.WriteNumber("durationSeconds", state.DurationSeconds);
        writer.WriteEndObject();
    }

    private static void WriteOptional(Utf8JsonWriter writer, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private string NextId() => "macos-" + Interlocked.Increment(ref sequence);
    private void Write(string line) { lock (io) host?.StandardInput.WriteLine(line); }
    private static string? Text(JsonElement value, string name) => value.TryGetProperty(name, out var property)
        && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private async Task StopHostAsync()
    {
        var process = host;
        host = null; published = null; pending = null; pendingId = null;
        ResetAccessoryAck();
        if (process is null) return;
        try
        {
            if (!process.HasExited) { lock (io) process.StandardInput.WriteLine("{\"type\":\"shutdown\",\"requestId\":\"bye\"}"); }
            if (!process.WaitForExit(1500)) process.Kill(entireProcessTree: true);
        }
        catch (Exception) { try { process.Kill(entireProcessTree: true); } catch (Exception) { } }
        if (reader is not null) await reader.ConfigureAwait(false);
        process.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        wake.Writer.TryComplete();
        await lifetime.CancelAsync().ConfigureAwait(false);
        try { await worker.ConfigureAwait(false); } catch (Exception) { }
        lifetime.Dispose();
        Command = null; DiagnosticChanged = null; FilesOpened = null;
    }
}
