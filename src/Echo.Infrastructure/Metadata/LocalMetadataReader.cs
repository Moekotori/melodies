using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

public sealed class LocalMetadataReader(string? nativeScanner = null) : IMetadataReader
{
    private NativeMetadataReader? _native;
    private readonly SourceMetadataReader _sources = new();
    public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken cancellation)
    {
        if (file.Source is { } source)
            return await _sources.ReadAsync(file, source, ReadPhysicalAsync, cancellation).ConfigureAwait(false);
        return await ReadPhysicalAsync(file, false, cancellation).ConfigureAwait(false);
    }
    private async ValueTask<TrackMetadata> ReadPhysicalAsync(FileSnapshot file, bool osu, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(file.Path);
        if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) || extension.Equals(".osu", StringComparison.OrdinalIgnoreCase))
            throw new MetadataReadException("virtual_tracks_not_migrated");
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) return WaveMetadataReader.Read(file, cancellation);
        if (extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fla", StringComparison.OrdinalIgnoreCase))
            return FlacMetadataReader.Read(file, cancellation);
        var scanner = nativeScanner ?? NativeScannerLocator.Find();
        if (scanner is null) throw new MetadataReadException("native_scanner_required");
        _native ??= new(scanner);
        return await _native.ReadAsync(file, cancellation, osu).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() => _native?.DisposeAsync() ?? ValueTask.CompletedTask;
}
