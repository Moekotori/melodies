using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

public sealed class LocalMetadataReader(string? nativeScanner = null) : IMetadataReader
{
    private NativeMetadataReader? _native;
    public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(file.Path);
        if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase) || extension.Equals(".osu", StringComparison.OrdinalIgnoreCase))
            throw new MetadataReadException("virtual_tracks_not_migrated");
        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)) return WaveMetadataReader.Read(file, cancellation);
        if (extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) || extension.Equals(".fla", StringComparison.OrdinalIgnoreCase))
            return FlacMetadataReader.Read(file, cancellation);
        if (nativeScanner is null) throw new MetadataReadException("native_scanner_required");
        _native ??= new(nativeScanner);
        return await _native.ReadAsync(file, cancellation).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync() => _native?.DisposeAsync() ?? ValueTask.CompletedTask;
}
