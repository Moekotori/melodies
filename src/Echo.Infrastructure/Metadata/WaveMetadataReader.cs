using System.Buffers.Binary;
using System.Text;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

// Read headers and bounded INFO tags; seek over PCM and image/unknown chunks.
internal static class WaveMetadataReader
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static TrackMetadata Read(FileSnapshot file, CancellationToken cancellation)
    {
        using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.RandomAccess);
        Span<byte> header = stackalloc byte[16];
        Span<byte> extension = stackalloc byte[24];
        Span<byte> type = stackalloc byte[4];
        Span<byte> tagHeader = stackalloc byte[8];
        stream.ReadExactly(header[..12]);
        if (!header[..4].SequenceEqual("RIFF"u8) || !header[8..12].SequenceEqual("WAVE"u8))
            throw new MetadataReadException("unsupported_wave_container");
        int sampleRate = 0, bits = 0, bytesPerSecond = 0;
        var codec = "PCM";
        long dataBytes = -1;
        var title = ""; var artist = ""; var album = ""; var genre = "";
        int? year = null, trackNumber = null;
        var chunks = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++chunks > 1024) throw new MetadataReadException("wave_chunk_limit");
            stream.ReadExactly(header[..8]);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
            var start = stream.Position;
            var end = checked(start + length);
            if (end > stream.Length) throw new MetadataReadException("truncated_wave_chunk");
            if (header[..4].SequenceEqual("fmt "u8))
            {
                if (length < 16) throw new MetadataReadException("invalid_wave_format");
                stream.ReadExactly(header);
                var format = BinaryPrimitives.ReadUInt16LittleEndian(header);
                if (format is not (1 or 3 or 0xfffe)) throw new MetadataReadException("unsupported_wave_codec");
                sampleRate = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]));
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(header[2..4]);
                bytesPerSecond = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(header[8..12]));
                var alignment = BinaryPrimitives.ReadUInt16LittleEndian(header[12..14]);
                if (channels == 0 || alignment == 0 || (long)sampleRate * alignment != bytesPerSecond)
                    throw new MetadataReadException("invalid_wave_alignment");
                bits = BinaryPrimitives.ReadUInt16LittleEndian(header[14..16]);
                if (format == 3) codec = "PCM_FLOAT";
                if (format == 0xfffe)
                {
                    if (length < 40) throw new MetadataReadException("invalid_wave_extensible");
                    stream.ReadExactly(extension);
                    var subtype = BinaryPrimitives.ReadUInt32LittleEndian(extension[8..12]);
                    if (subtype is not (1 or 3)) throw new MetadataReadException("unsupported_wave_codec");
                    ReadOnlySpan<byte> suffix = [0,0,0x10,0,0x80,0,0,0xaa,0,0x38,0x9b,0x71];
                    if (!extension[12..24].SequenceEqual(suffix)) throw new MetadataReadException("unsupported_wave_codec");
                    if (subtype == 3) codec = "PCM_FLOAT";
                    var validBits = BinaryPrimitives.ReadUInt16LittleEndian(extension[2..4]);
                    if (validBits > bits) throw new MetadataReadException("invalid_wave_bit_depth");
                    if (validBits != 0) bits = validBits;
                }
            }
            else if (header[..4].SequenceEqual("data"u8)) dataBytes = length;
            else if (header[..4].SequenceEqual("LIST"u8) && length >= 4)
            {
                stream.ReadExactly(type);
                if (type.SequenceEqual("INFO"u8))
                {
                    var tags = 0;
                    while (stream.Position + 8 <= end)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (++tags > 256) throw new MetadataReadException("wave_tag_limit");
                        stream.ReadExactly(tagHeader);
                        var tagLength = BinaryPrimitives.ReadUInt32LittleEndian(tagHeader[4..8]);
                        var tagEnd = checked(stream.Position + tagLength);
                        if (tagEnd > end) throw new MetadataReadException("truncated_wave_tag");
                        var relevant = tagHeader[..4].SequenceEqual("INAM"u8) || tagHeader[..4].SequenceEqual("IART"u8) ||
                            tagHeader[..4].SequenceEqual("IPRD"u8) || tagHeader[..4].SequenceEqual("IGNR"u8) ||
                            tagHeader[..4].SequenceEqual("ICRD"u8) || tagHeader[..4].SequenceEqual("ITRK"u8);
                        if (relevant)
                        {
                            if (tagLength > 16 * 1024) throw new MetadataReadException("wave_tag_size_limit");
                            var bytes = new byte[(int)tagLength];
                            stream.ReadExactly(bytes);
                            // INFO has no universal legacy codepage declaration; UTF-8 is the deterministic default.
                            string value;
                            try { value = Utf8.GetString(bytes).TrimEnd('\0'); }
                            catch (DecoderFallbackException) { throw new MetadataReadException("wave_tag_encoding"); }
                            if (tagHeader[..4].SequenceEqual("INAM"u8)) title = value;
                            else if (tagHeader[..4].SequenceEqual("IART"u8)) artist = value;
                            else if (tagHeader[..4].SequenceEqual("IPRD"u8)) album = value;
                            else if (tagHeader[..4].SequenceEqual("IGNR"u8)) genre = value;
                            else if (tagHeader[..4].SequenceEqual("ICRD"u8) && int.TryParse(value.AsSpan(0, Math.Min(4, value.Length)), out var y)) year = y;
                            else if (tagHeader[..4].SequenceEqual("ITRK"u8) && int.TryParse(value.Split('/')[0], out var n)) trackNumber = n;
                        }
                        stream.Position = tagEnd + (tagLength & 1);
                    }
                }
            }
            else if (header[..4].SequenceEqual("id3 "u8) || header[..4].SequenceEqual("ID3 "u8))
                throw new MetadataReadException("wave_extended_tags");
            stream.Position = end + (length & 1);
        }
        if (dataBytes < 0 || bytesPerSecond <= 0 || sampleRate <= 0 || bits <= 0)
            throw new MetadataReadException("invalid_wave_properties");
        return MetadataRules.Normalize(file, new(title, artist, album, "", (double)dataBytes / bytesPerSecond,
            codec, sampleRate, bits, trackNumber, Year: year, Genre: genre, Bitrate: (int)Math.Min((long)bytesPerSecond * 8, int.MaxValue)));
    }
}
