using System.Text;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

// Same BOM / UTF-8 / GB18030 fallback as legacy ScanCueReader and decodeTextFileBytes.
internal static class LegacyTextEncoding
{
    public static string Read(string path, int maxBytes, CancellationToken cancellation)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maxBytes) throw new MetadataReadException("source_text_limit");
        var data = new byte[(int)stream.Length];
        cancellation.ThrowIfCancellationRequested();
        stream.ReadExactly(data);
        cancellation.ThrowIfCancellationRequested();
        if (data.AsSpan().StartsWith(new byte[] { 239,187,191 })) return Encoding.UTF8.GetString(data).TrimStart('\uFEFF');
        if (data.AsSpan().StartsWith(new byte[] { 255,254 })) return Encoding.Unicode.GetString(data).TrimStart('\uFEFF');
        if (data.AsSpan().StartsWith(new byte[] { 254,255 })) return Encoding.BigEndianUnicode.GetString(data).TrimStart('\uFEFF');
        try { return new UTF8Encoding(false, true).GetString(data); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding("GB18030").GetString(data);
        }
    }
}
