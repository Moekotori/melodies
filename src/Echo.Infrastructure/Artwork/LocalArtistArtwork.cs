namespace Echo.Infrastructure.Artwork;

public sealed partial class LocalAlbumArtwork
{
    public async Task<byte[]?> ReadArtistAsync(string samplePath, CancellationToken cancellation)
    {
        var directory = Path.GetDirectoryName(samplePath);
        if (directory is null) return null;
        foreach (var name in new[] { "artist.jpg", "artist.png", "artist.webp" })
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                await using var file = Open(Path.Combine(directory, name));
                if (file.Length is <= 0 or > MaxBytes) continue;
                var bytes = new byte[(int)file.Length];
                await file.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false); return bytes;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }
}
