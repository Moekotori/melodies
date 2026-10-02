using System.Diagnostics;
using Echo.Contracts.Library;

namespace Echo.Infrastructure.Artwork;

// One request per active detail view; bounded directory discovery and text reads run off the UI thread.
public sealed class LocalDetailResources : IDetailResources
{
    public Task<AttachmentPage> ReadAttachmentsAsync(string samplePath, CancellationToken cancellation) => Task.Run(() =>
    {
        var directory = Path.GetDirectoryName(samplePath);
        if (directory is null || !Directory.Exists(directory)) return new AttachmentPage([], false);
        var items = new List<AlbumAttachment>(); var examined = 0; var truncated = false;
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            cancellation.ThrowIfCancellationRequested();
            if (++examined > 512 || items.Count == 32) { truncated = true; break; }
            var extension = Path.GetExtension(file).ToLowerInvariant();
            var image = extension is ".jpg" or ".jpeg" or ".png" or ".webp";
            if (!image && extension is not (".pdf" or ".txt" or ".cue" or ".log")) continue;
            items.Add(new(Path.GetFileName(file), file, image));
        }
        return new AttachmentPage(items.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(), truncated);
    }, cancellation);

    public Task<string?> ReadDescriptionAsync(string samplePath, CancellationToken cancellation) => Task.Run(async () =>
    {
        var directory = Path.GetDirectoryName(samplePath);
        if (directory is null) return null;
        foreach (var name in new[] { "album.txt", "readme.txt", "description.txt" })
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(Path.Combine(directory, name), FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
                if (stream.Length > 64 * 1024) continue;
                using var reader = new StreamReader(stream);
                var text = await reader.ReadToEndAsync(cancellation).ConfigureAwait(false);
                return text.Trim();
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return null;
    }, cancellation);

    public Task OpenAsync(string path, CancellationToken cancellation) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(path) || (!File.Exists(path) && !Directory.Exists(path)))
            throw new IOException("File is no longer available.");
        // Attachments are passive images/text/documents, never executables or shortcut files.
        if (File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".pdf" or ".txt" or ".cue" or ".log"))
            throw new ArgumentException("Unsupported attachment.");
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }, cancellation);
}
