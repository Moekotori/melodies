using System.Text;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Metadata;

internal static class FlacVariants
{
    // Real encoded frames from the shared FLAC fixture, with valid uncommon metadata layouts.
    public static async Task CheckAsync(string original, string root, string scanner, CancellationToken token)
    {
        var source = await File.ReadAllBytesAsync(original, token);
        var id3 = Path.Combine(root, "id3-prefix.flac");
        await using (var file = File.Create(id3))
        {
            await file.WriteAsync(new byte[] { 73, 68, 51, 4, 0, 0, 0, 0, 0, 0 }, token);
            await file.WriteAsync(source, token);
        }
        var large = Path.Combine(root, "large-comments.flac");
        var many = Path.Combine(root, "many-comments.flac");
        void ReplaceComments(string path, bool largeVendor)
        {
            using var comments = new MemoryStream();
            using (var writer = new BinaryWriter(comments, Encoding.UTF8, true))
            {
                var vendor = largeVendor ? new byte[300 * 1024] : "ECHO"u8.ToArray();
                if (largeVendor) vendor.AsSpan().Fill((byte)'x');
                writer.Write(vendor.Length); writer.Write(vendor);
                var count = largeVendor ? 1 : 520; writer.Write(count);
                for (var i = 0; i < count; i++)
                {
                    var text = Encoding.UTF8.GetBytes(i == 0 ? "TITLE=Variant" : $"ECHO_{i}=tag");
                    writer.Write(text.Length); writer.Write(text);
                }
            }
            using var output = File.Create(path); output.Write(source.AsSpan(0, 4));
            var offset = 4;
            while (true)
            {
                var type = source[offset];
                var length = (source[offset + 1] << 16) | (source[offset + 2] << 8) | source[offset + 3];
                if ((type & 127) == 4)
                {
                    var bytes = comments.ToArray();
                    output.Write([type, (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 8), (byte)bytes.Length]);
                    output.Write(bytes);
                }
                else output.Write(source.AsSpan(offset, 4 + length));
                offset += 4 + length;
                if ((type & 128) == 0) continue;
                output.Write(source.AsSpan(offset)); break;
            }
        }
        ReplaceComments(large, true); ReplaceComments(many, false);
        await using var reader = new LocalMetadataReader(scanner);
        foreach (var path in new[] { id3, large, many })
        {
            var info = new FileInfo(path);
            var metadata = await reader.ReadAsync(new(path, LibraryPath.Key(path), info.Length, info.LastWriteTimeUtc.Ticks), token);
            if (metadata.DurationSeconds <= 0 || metadata.SampleRate != 48000 || metadata.Codec != "FLAC")
                throw new Exception("FLAC native fallback lost audio properties: " + Path.GetFileName(path));
            Console.WriteLine("PASS native FLAC fallback: " + Path.GetFileName(path));
        }
    }
}
