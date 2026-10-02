using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Echo.Contracts.Shortcuts;

namespace Echo.Infrastructure.Preferences;

// Separate from old ECHO settings. One writer and one coalesced pending profile, max 32 KiB.
public sealed class JsonShortcutProfileStore : IShortcutProfileStore
{
    private readonly string _path;
    private readonly Channel<ShortcutProfile> _pending = Channel.CreateBounded<ShortcutProfile>(new BoundedChannelOptions(1)
        { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly Task _writer;
    public event Action<string>? Error;
    public JsonShortcutProfileStore(string path) { _path = Path.GetFullPath(path); _writer = Task.Run(WriteAsync); }
    public Task<ShortcutProfile?> ReadAsync(CancellationToken cancellation) => Task.Run(async () =>
    {
        if (!File.Exists(_path)) return null;
        await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length > 32768) throw new InvalidDataException("Shortcut profile exceeds limit.");
        var result = await JsonSerializer.DeserializeAsync(stream, ShortcutProfileJsonContext.Default.ShortcutProfile, cancellation).ConfigureAwait(false);
        if (result is not { Version: 1, Bindings.Count: <= 66 }) throw new InvalidDataException("Unsupported shortcut profile.");
        return result;
    }, cancellation);
    public void Save(ShortcutProfile profile) => _pending.Writer.TryWrite(profile);
    private async Task WriteAsync()
    {
        await foreach (var profile in _pending.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(profile, ShortcutProfileJsonContext.Default.ShortcutProfile);
                if (bytes.Length > 32768) throw new InvalidDataException("Shortcut profile exceeds limit.");
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                File.Move(temporary, _path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            { Error?.Invoke("快捷键未能保存，请检查设置目录权限后重试。"); }
            finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
    public async ValueTask DisposeAsync() { _pending.Writer.TryComplete(); await _writer.ConfigureAwait(false); Error = null; }
}

[JsonSerializable(typeof(ShortcutProfile))]
internal partial class ShortcutProfileJsonContext : JsonSerializerContext;
