using Echo.Contracts.Library;
using Echo.Infrastructure.Artwork;
using SkiaSharp;

internal sealed class ArtistArtworkFixture : IAlbumArtwork, IArtistArtwork
{
    private readonly LocalAlbumArtwork local = new();
    internal int LocalPortraits, OnlinePortraits;
    public Task<byte[]?> ReadAsync(string path, CancellationToken token) => local.ReadAsync(path, token);
    public Task<byte[]?> ReadArtistAsync(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (path == "online:fixture")
        {
            Interlocked.Increment(ref OnlinePortraits);
            return Task.FromResult<byte[]?>(Pixels());
        }
        Interlocked.Increment(ref LocalPortraits); return local.ReadArtistAsync(path, token);
    }
    internal static byte[] Pixels()
    {
        using var bitmap = new SKBitmap(16, 16); bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
