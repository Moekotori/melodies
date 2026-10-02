using System.IO.Compression;
using System.Text;
using Echo.Contracts.Lyrics;

namespace Echo.Infrastructure.Lyrics;

internal static class LocalLyricsReader
{
    private const int MaxBytes = 1024 * 1024;
    private const int MaxCharacters = 512 * 1024;
    private static readonly byte[] KrcKey = [0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69];

    public static async Task<LyricsText> ReadAsync(string path, CancellationToken cancellation)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var bytes = await ReadBoundedAsync(stream, cancellation).ConfigureAwait(false);
        var format = Path.GetExtension(path);
        if (bytes.AsSpan().StartsWith("krc1"u8))
        {
            format = ".krc";
            for (var i = 4; i < bytes.Length; i++) bytes[i] ^= KrcKey[(i - 4) % KrcKey.Length];
            using var compressed = new MemoryStream(bytes, 4, bytes.Length - 4, false);
            using var inflater = new ZLibStream(compressed, CompressionMode.Decompress);
            bytes = await ReadBoundedAsync(inflater, cancellation).ConfigureAwait(false);
        }
        var text = Decode(bytes);
        if (text.Length > MaxCharacters) throw new InvalidDataException("歌词文本超过 512 KiB。");
        return new(text, format, Path.GetFileName(path));
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, CancellationToken cancellation)
    {
        if (stream.CanSeek && stream.Length > MaxBytes) throw new InvalidDataException("歌词文件超过 1 MiB。");
        using var output = new MemoryStream(); var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), cancellation).ConfigureAwait(false)) > 0)
        {
            if (output.Length + count > MaxBytes) throw new InvalidDataException("歌词文件或解压内容超过 1 MiB。");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        // BOMs are authoritative; never retry a damaged BOM-marked file as a legacy encoding.
        if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return new UTF32Encoding(false, false, true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return new UTF32Encoding(true, false, true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
                ?? throw new InvalidDataException("无法读取歌词编码。");
            return encoding.GetString(bytes);
        }
    }
}
