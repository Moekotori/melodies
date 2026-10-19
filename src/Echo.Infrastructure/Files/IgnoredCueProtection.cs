using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Files;

internal static class IgnoredCueProtection
{
    public static IEnumerable<FileSnapshot> Read(FileSnapshot definition, ILibraryStore? store,
        FolderSourceSet suppressed, FolderSourceSet? deferred, CancellationToken cancellation)
    {
        if (store is null) yield break;
        string? after = null;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var page = store.ReadDefinitionTracksAsync(definition.Path, after, cancellation).GetAwaiter().GetResult();
            if (page.Count == 0) yield break;
            foreach (var file in page)
            {
                if (file.Source is { } source) suppressed.Add(LibraryPath.Key(source.AudioPath));
                else deferred?.Add(LibraryPath.Key(Path.GetDirectoryName(definition.Path)!));
                yield return file with { Source = null };
            }
            after = page[^1].PathKey;
        }
    }
}
