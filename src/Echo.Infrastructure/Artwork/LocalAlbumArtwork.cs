using System.Buffers.Binary;
using Echo.Contracts.Library;

namespace Echo.Infrastructure.Artwork;

// Invoked only by the fixed artwork worker, never by layout or bindings.
public sealed partial class LocalAlbumArtwork : IAlbumArtwork, IArtistArtwork
{
    private const int MaxBytes = 4 * 1024 * 1024;
    private static readonly string[] Names = ["cover.jpg", "cover.png", "folder.jpg", "folder.png", "front.jpg", "front.png", "album.jpg", "album.png", "cover.webp"];

    public async Task<byte[]?> ReadAsync(string samplePath, CancellationToken cancellation)
    {
        var directory = Path.GetDirectoryName(samplePath);
        if (directory is null) return null;
        foreach (var name in Names)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                await using var file = Open(Path.Combine(directory, name));
                if (file.Length is <= 0 or > MaxBytes) continue;
                var bytes = new byte[(int)file.Length];
                await file.ReadExactlyAsync(bytes, cancellation).ConfigureAwait(false);
                return bytes;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // FLAC picture blocks are bounded metadata; never read PCM or the entire audio file.
        var extension = Path.GetExtension(samplePath);
        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase))
            return await ReadId3PictureAsync(samplePath, cancellation).ConfigureAwait(false);
        if (!extension.Equals(".flac", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            await using var file = Open(samplePath);
            var header = new byte[4];
            await file.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
            if (!header.AsSpan().SequenceEqual("fLaC"u8)) return null;
            for (var blocks = 0; blocks < 128; blocks++)
            {
                await file.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
                var size = (header[1] << 16) | (header[2] << 8) | header[3];
                if ((header[0] & 127) == 6 && size <= MaxBytes)
                {
                    var picture = new byte[size];
                    await file.ReadExactlyAsync(picture, cancellation).ConfigureAwait(false);
                    return ExtractFlacPicture(picture);
                }
                file.Seek(size, SeekOrigin.Current);
                if ((header[0] & 128) != 0) break;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    private static async Task<byte[]?> ReadId3PictureAsync(string path, CancellationToken cancellation)
    {
        try
        {
            await using var file = Open(path);
            var header = new byte[10];
            await file.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
            if (!header.AsSpan(0, 3).SequenceEqual("ID3"u8) || header[3] is not (3 or 4) || (header[5] & 0xc0) != 0) return null;
            var size = Synchsafe(header.AsSpan(6, 4));
            if (size is < 10 or > MaxBytes) return null;
            var tag = new byte[size];
            await file.ReadExactlyAsync(tag, cancellation).ConfigureAwait(false);
            return ExtractId3Picture(tag, header[3] == 4);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static byte[]? ExtractId3Picture(ReadOnlySpan<byte> tag, bool v4)
    {
        for (var offset = 0; offset + 10 <= tag.Length;)
        {
            var frame = tag[offset..];
            var size = v4 ? Synchsafe(frame.Slice(4, 4)) : (long)BinaryPrimitives.ReadUInt32BigEndian(frame[4..]);
            if (size <= 0 || size > tag.Length - offset - 10) return null;
            if (frame[..4].SequenceEqual("APIC"u8) && frame[9] == 0)
            {
                var data = frame.Slice(10, (int)size);
                if (data.Length < 4) return null;
                var encoding = data[0];
                var mimeEnd = data[1..].IndexOf((byte)0);
                if (mimeEnd < 0) return null;
                var picture = mimeEnd + 3;
                if (encoding is 1 or 2)
                {
                    while (picture + 1 < data.Length && (data[picture] != 0 || data[picture + 1] != 0)) picture += 2;
                    picture += 2;
                }
                else
                {
                    if (picture >= data.Length) return null;
                    var end = data[picture..].IndexOf((byte)0);
                    if (end < 0) return null;
                    picture += end + 1;
                }
                return picture < data.Length ? data[picture..].ToArray() : null;
            }
            offset += 10 + (int)size;
        }
        return null;
    }
    private static int Synchsafe(ReadOnlySpan<byte> value)
        => (value[0] | value[1] | value[2] | value[3]) < 128
            ? (value[0] << 21) | (value[1] << 14) | (value[2] << 7) | value[3] : -1;

    private static byte[]? ExtractFlacPicture(ReadOnlySpan<byte> block)
    {
        if (block.Length < 12) return null;
        var mimeLength = BinaryPrimitives.ReadUInt32BigEndian(block[4..]);
        if (mimeLength > block.Length - 12) return null;
        var offset = 8 + (int)mimeLength;
        var descriptionLength = BinaryPrimitives.ReadUInt32BigEndian(block[offset..]);
        if (descriptionLength > block.Length - offset - 24) return null;
        offset += 4 + (int)descriptionLength + 16;
        var length = BinaryPrimitives.ReadUInt32BigEndian(block[offset..]);
        offset += 4;
        return length <= block.Length - offset ? block.Slice(offset, (int)length).ToArray() : null;
    }

    private static FileStream Open(string path) => new(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
}
