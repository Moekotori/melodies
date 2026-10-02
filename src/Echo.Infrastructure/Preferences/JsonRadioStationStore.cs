using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Radio;

namespace Echo.Infrastructure.Preferences;

// The application catalog is the only writer. Atomic replacement also preserves an empty list.
public sealed class JsonRadioStationStore(string path) : IRadioStationStore
{
    public Task<IReadOnlyList<RadioStation>?> ReadAsync(CancellationToken token) => Task.Run<IReadOnlyList<RadioStation>?>(async () =>
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            if (stream.Length > 1024 * 1024) throw new IOException("电台资料文件过大。");
            return await JsonSerializer.DeserializeAsync(stream, RadioStoreJsonContext.Default.RadioStationArray, token)
                .ConfigureAwait(false) ?? throw new IOException("电台资料文件无效。");
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }, token);

    public async Task WriteAsync(IReadOnlyList<RadioStation> stations, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(stations.ToArray(), RadioStoreJsonContext.Default.RadioStationArray);
        if (bytes.Length > 1024 * 1024) throw new IOException("电台资料不能超过 1 MiB，请缩短简介或减少电台。");
        await Task.Run(async () =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }, token).ConfigureAwait(false);
    }
}

[JsonSerializable(typeof(RadioStation[]))]
internal partial class RadioStoreJsonContext : JsonSerializerContext;
