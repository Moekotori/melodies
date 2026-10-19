using System.Buffers.Binary;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal sealed record WaveFormatInfo(int Rate, int Bits, int Alignment, long BytesPerSecond, string Codec)
{
    public static WaveFormatInfo Read(Stream stream, long length, bool bigEndian)
    {
        if (length < 16) throw new MetadataReadException("invalid_wave_format");
        Span<byte> bytes = stackalloc byte[40]; stream.ReadExactly(bytes[..16]);
        var format = U16(bytes, bigEndian);
        if (format is not (1 or 3 or 0xfffe)) throw new MetadataReadException("unsupported_wave_codec");
        var channels = U16(bytes[2..], bigEndian);
        var rate = U32(bytes[4..], bigEndian);
        var byteRate = U32(bytes[8..], bigEndian);
        var alignment = U16(bytes[12..], bigEndian);
        var containerBits = U16(bytes[14..], bigEndian);
        int bits = containerBits;
        if (format == 0xfffe)
        {
            if (bigEndian) throw new MetadataReadException("unsupported_wave_codec");
            if (length < 40) throw new MetadataReadException("invalid_wave_extensible");
            stream.ReadExactly(bytes[16..]);
            var extra = BinaryPrimitives.ReadUInt16LittleEndian(bytes[16..]);
            if (extra < 22 || 18L + extra > length) throw new MetadataReadException("invalid_wave_extensible");
            var subtype = BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..]);
            ReadOnlySpan<byte> suffix = [0, 0, 0x10, 0, 0x80, 0, 0, 0xaa, 0, 0x38, 0x9b, 0x71];
            if (subtype is not (1 or 3) || !bytes[28..].SequenceEqual(suffix)) throw new MetadataReadException("unsupported_wave_codec");
            format = (ushort)subtype;
            var valid = BinaryPrimitives.ReadUInt16LittleEndian(bytes[18..]);
            if (valid > containerBits) throw new MetadataReadException("invalid_wave_bit_depth");
            if (valid > 0) bits = valid;
        }
        if (containerBits is not (8 or 16 or 24 or 32 or 64) || format == 3 && containerBits is not (32 or 64))
            throw new MetadataReadException("unsupported_wave_codec");
        if (channels == 0 || alignment != (long)channels * containerBits / 8 || byteRate != (long)rate * alignment)
            throw new MetadataReadException("invalid_wave_alignment");
        if (rate is 0 or > int.MaxValue || byteRate == 0 || bits == 0) throw new MetadataReadException("invalid_wave_properties");
        var codec = format == 3 ? "PCM_FLOAT" : "PCM";
        if (bigEndian && containerBits > 8) codec += "_BE";
        return new((int)rate, bits, alignment, byteRate, codec);
    }

    private static ushort U16(ReadOnlySpan<byte> bytes, bool big) => big ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    private static uint U32(ReadOnlySpan<byte> bytes, bool big) => big ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
}
