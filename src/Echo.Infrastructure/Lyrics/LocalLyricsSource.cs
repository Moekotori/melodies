using System.Text;
using Echo.Contracts.Lyrics;

namespace Echo.Infrastructure.Lyrics;

public sealed class LocalLyricsSource : ILyricsSource, IAsyncDisposable
{
    private static readonly string[] Extensions = [".ttml", ".yrc", ".lrc", ".srt", ".vtt", ".txt"];
    private readonly SemaphoreSlim reader = new(1);
    public async Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation)
    {
        await reader.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                var candidates = explicitPath is null ? Extensions.Select(ext => Path.ChangeExtension(audioPath, ext)) : [explicitPath];
                foreach (var path in candidates)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        if (stream.Length > 1024 * 1024) throw new InvalidDataException("歌词文件超过 1 MiB，请使用较小的文件。");
                        using var text = new StreamReader(stream, new UTF8Encoding(false, true), true);
                        var buffer = new char[8192]; var content = new StringBuilder();
                        int count;
                        while ((count = await text.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
                        {
                            if (content.Length + count > 512 * 1024) throw new InvalidDataException("歌词文本超过 512 KiB。");
                            content.Append(buffer, 0, count);
                        }
                        return new LyricsText(content.ToString(), Path.GetExtension(path), Path.GetFileName(path));
                    }
                    catch (FileNotFoundException) when (explicitPath is null) { }
                    catch (DirectoryNotFoundException) when (explicitPath is null) { }
                }
                return null;
            }, cancellation).ConfigureAwait(false);
        }
        finally { reader.Release(); }
    }
    public async ValueTask DisposeAsync()
    { await reader.WaitAsync().ConfigureAwait(false); reader.Dispose(); }
}
