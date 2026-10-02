using System.IO.Enumeration;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Files;

public sealed class DirectoryDiscovery : IFileDiscovery
{
    internal static bool Supports(string extension) => ScanFormats.Supports(extension);

    public IEnumerable<FileSnapshot> Enumerate(string root, bool requireFullCoverage, CancellationToken cancellation, bool force = false)
    {
        root = LibraryPath.Canonical(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Scan root is unavailable.");
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true, IgnoreInaccessible = false,
            AttributesToSkip = 0, ReturnSpecialDirectories = false,
            BufferSize = 16 * 1024
        };
        // Size and mtime come from enumeration, avoiding a separate FileInfo/stat per unchanged file.
        var entries = new FileSystemEnumerable<FileSnapshot>(root, Transform, options)
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if (entry.IsDirectory) return false;
                var extension = Path.GetExtension(entry.FileName);
                if (ScanFormats.Supports(extension))
                {
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        if (requireFullCoverage) throw new IOException("Missing detection cannot exclude linked media.");
                        return false;
                    }
                    return true;
                }
                return false;
            },
            ShouldRecursePredicate = (ref FileSystemEntry entry) =>
            {
                cancellation.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) == 0) return true;
                if (requireFullCoverage) throw new IOException("Missing detection cannot exclude linked directories.");
                return false;
            }
        };
        foreach (var entry in entries)
        {
            cancellation.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    private static FileSnapshot Transform(ref FileSystemEntry entry)
    {
        var path = LibraryPath.Canonical(entry.ToFullPath());
        var unavailable = ((int)entry.Attributes & ((int)FileAttributes.Offline | 0x40000 | 0x400000)) != 0;
        return new(path, LibraryPath.Key(path), entry.Length, entry.LastWriteTimeUtc.UtcTicks, unavailable);
    }
}
