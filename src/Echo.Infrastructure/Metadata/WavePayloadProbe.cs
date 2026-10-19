using System.Buffers.Binary;

namespace Echo.Infrastructure.Metadata;

internal static class WavePayloadProbe
{
    public static bool IsEncoded(ReadOnlySpan<byte> prefix)
    {
        // Check word boundaries: four DTS sync representations and both IEC61937 preamble orders.
        for (var i = 0; i + 4 <= prefix.Length; i += 2)
        {
            var sync = BinaryPrimitives.ReadUInt32BigEndian(prefix[i..]);
            if (sync is 0x7ffe8001 or 0xfe7f0180 or 0x1fffe800 or 0xff1f00e8 or 0x72f81f4e or 0xf8724e1f)
                return true;
        }
        return false;
    }
}
