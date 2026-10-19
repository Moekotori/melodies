using System.Diagnostics;
using Echo.Contracts.Library;

namespace Echo.Infrastructure.Artwork;

// One request per active detail view; bounded directory discovery and text reads run off the UI thread.
public sealed class LocalDetailResources : IDetailResources
{
    public Task<AttachmentPage> ReadAttachmentsAsync(string samplePath, CancellationToken cancellation) => Task.Run(() =>
    {
        var directory = Path.GetDirectoryName(samplePath);
        if (directory is null || !Directory.Exists(directory)) throw new DirectoryNotFoundException("Album folder is unavailable.");
        var items = new List<AlbumAttachment>(); var examined = 0; var truncated = false;
        var folders = new Queue<(string Path, int Depth)>(); folders.Enqueue((directory, 0));
        var visited = 0;
        while (folders.TryDequeue(out var folder) && items.Count < 32)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++visited > 24) { truncated = true; break; }
            try
            {
                if (folder.Depth < 2)
                {
                    foreach (var child in Directory.EnumerateDirectories(folder.Path).Take(32))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                        var name = Path.GetFileName(child).ToLowerInvariant();
                        if (folder.Depth > 0 || name is "scans" or "scan" or "booklet" or "artwork" or "artworks" or "covers" or "images" or "内页" or "封面")
                        { if (folders.Count < 24) folders.Enqueue((child, folder.Depth + 1)); else truncated = true; }
                    }
                }
                var inFolder = 0;
                foreach (var file in Directory.EnumerateFiles(folder.Path))
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++inFolder > 512 || ++examined > 2048 || items.Count == 32) { truncated = true; break; }
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                    var extension = Path.GetExtension(file).ToLowerInvariant();
                    var image = extension is ".jpg" or ".jpeg" or ".png" or ".webp";
                    if (!image && extension is not (".pdf" or ".txt" or ".cue" or ".log")) continue;
                    items.Add(new(Path.GetRelativePath(directory, file), file, image));
                }
            }
            catch (Exception error) when (folder.Depth > 0 && error is IOException or UnauthorizedAccessException) { truncated = true; }
            if (examined > 2048) break;
        }
        truncated |= folders.Count > 0;
        return new AttachmentPage(items.OrderBy(item => !item.IsImage).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray(), truncated);
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
