using Echo.Contracts.Lyrics;

namespace Echo.Infrastructure.Lyrics;

public sealed class LocalLyricsSource : ILyricsSource, IAsyncDisposable
{
    internal static readonly string[] Extensions = [".ttml", ".yrc", ".qrc", ".krc", ".lrc", ".srt", ".vtt", ".txt"];
    private readonly SemaphoreSlim reader = new(1);
    public async Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation)
    {
        await reader.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            return await Task.Run(async () =>
            {
                Exception? failure = null;
                var candidates = explicitPath is null ? Candidates(audioPath, cancellation) : [explicitPath];
                foreach (var path in candidates)
                {
                    cancellation.ThrowIfCancellationRequested();
                    try
                    {
                        var primary = await LocalLyricsReader.ReadAsync(path, cancellation).ConfigureAwait(false);
                        if (explicitPath is null && string.IsNullOrWhiteSpace(primary.Text)) continue;
                        var companions = LocalLyricsSidecars.Companions(path, cancellation);
                        return primary with
                        {
                            Translation = await ReadCompanionAsync(companions.Translation, cancellation).ConfigureAwait(false),
                            Romanization = await ReadCompanionAsync(companions.Romanization, cancellation).ConfigureAwait(false)
                        };
                    }
                    catch (FileNotFoundException) when (explicitPath is null) { }
                    catch (DirectoryNotFoundException) when (explicitPath is null) { }
                    catch (Exception error) when (explicitPath is null && error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                    { failure ??= error; }
                }
                if (failure is not null) throw failure;
                return null;
            }, cancellation).ConfigureAwait(false);
        }
        finally { reader.Release(); }
    }
    private static IEnumerable<string> Candidates(string audioPath, CancellationToken cancellation)
    {
        var folder = Path.GetDirectoryName(audioPath) ?? "";
        var name = Path.GetFileName(audioPath);
        var stem = Path.GetFileNameWithoutExtension(audioPath);
        foreach (var directory in new[] { folder, Path.Combine(folder, "lyrics") })
            foreach (var candidateStem in new[] { stem, name }.Distinct(StringComparer.Ordinal))
                foreach (var extension in Extensions) yield return Path.Combine(directory, candidateStem + extension);
        foreach (var path in LocalLyricsSidecars.Promoted(audioPath, cancellation)) yield return path;
    }
    private static async Task<LyricsTrackText?> ReadCompanionAsync(string? path, CancellationToken cancellation)
    {
        if (path is null) return null;
        try
        {
            var text = await LocalLyricsReader.ReadAsync(path, cancellation).ConfigureAwait(false);
            return new(text.Text, text.Format);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { return null; }
    }
    public async ValueTask DisposeAsync()
    { await reader.WaitAsync().ConfigureAwait(false); reader.Dispose(); }
}
