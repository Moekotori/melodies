using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Echo.Contracts.Lyrics;

namespace Echo.Infrastructure.Lyrics;

// One owner for this independent settings file. Capacity one replaces obsolete pending writes.
public sealed class LocalLyricsPreferencesStore : ILyricsPreferencesStore, IAsyncDisposable
{
    private readonly string path;
    private readonly Channel<LyricsPreferences> writes = Channel.CreateBounded<LyricsPreferences>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Task worker;
    public LocalLyricsPreferencesStore(string path) { this.path = path; worker = Task.Run(WriteAsync); }
    public Task<LyricsPreferences> ReadAsync(CancellationToken cancellation) => Task.Run(async () =>
    {
        try
        {
            await using var stream = File.OpenRead(path);
            if (stream.Length > 16384) return new LyricsPreferences();
            return await JsonSerializer.DeserializeAsync(stream, LyricsSettingsJson.Default.LyricsPreferences, cancellation).ConfigureAwait(false) ?? new();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }, cancellation);
    public void Save(LyricsPreferences preferences) => writes.Writer.TryWrite(preferences);
    private async Task WriteAsync()
    {
        await foreach (var value in writes.Reader.ReadAllAsync())
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                await using (var file = File.Create(temporary))
                    await JsonSerializer.SerializeAsync(file, value, LyricsSettingsJson.Default.LyricsPreferences).ConfigureAwait(false);
                File.Move(temporary, path, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
    public async ValueTask DisposeAsync() { writes.Writer.TryComplete(); await worker.ConfigureAwait(false); }
}
[JsonSerializable(typeof(LyricsPreferences))]
internal partial class LyricsSettingsJson : JsonSerializerContext;
