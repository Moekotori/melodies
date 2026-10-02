using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Echo.Contracts.Settings;

namespace Echo.Infrastructure.Preferences;

// One fixed worker, latest pending snapshot only, bounded theme library, max 512 KiB on disk.
public sealed class JsonDesktopPreferencesStore : IDesktopPreferencesStore
{
    private readonly string _path;
    private readonly Channel<DesktopPreferences> _pending = Channel.CreateBounded<DesktopPreferences>(new BoundedChannelOptions(1)
        { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _writer;
    private bool _disposed;
    public event Action<string>? Error;

    public JsonDesktopPreferencesStore(string path) { _path = Path.GetFullPath(path); _writer = Task.Run(WriteLoopAsync); }
    public Task<DesktopPreferences?> ReadAsync(CancellationToken cancellation) => Task.Run(async () =>
    {
        if (!File.Exists(_path)) return null;
        await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (file.Length > 524288) throw new InvalidDataException("Settings size limit exceeded.");
        var preferences = await JsonSerializer.DeserializeAsync(file, DesktopPreferencesJsonContext.Default.DesktopPreferences, cancellation).ConfigureAwait(false);
        if (preferences?.Version != 1) throw new InvalidDataException("Unsupported settings version.");
        return preferences;
    }, cancellation);
    public void Save(DesktopPreferences preferences) { if (!_disposed) _pending.Writer.TryWrite(preferences); }
    private async Task WriteLoopAsync()
    {
        await foreach (var preferences in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(preferences, DesktopPreferencesJsonContext.Default.DesktopPreferences);
                if (bytes.Length > 524288) throw new InvalidDataException("Settings size limit exceeded.");
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
                    await file.WriteAsync(bytes).ConfigureAwait(false);
                File.Move(temporary, _path, true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            { Error?.Invoke("设置未能保存，本次调整仍保留在当前窗口。"); }
            finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _pending.Writer.TryComplete(); await _writer.ConfigureAwait(false); Error = null;
    }
}

[JsonSerializable(typeof(DesktopPreferences))]
internal partial class DesktopPreferencesJsonContext : JsonSerializerContext;
