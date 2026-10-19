using System.Buffers.Binary;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

// Metadata only: seek over sample data and images, including 64-bit RF64/BW64 chunks.
internal static class WaveMetadataReader
{
    public static TrackMetadata Read(FileSnapshot file, CancellationToken cancellation)
    {
        using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.RandomAccess);
        var chunks = new WaveChunks(stream);
        var tags = new WaveTags();
        WaveFormatInfo? format = null;
        long dataBytes = -1, dataOffset = 0;
        Span<byte> type = stackalloc byte[8];
        while (chunks.MoveNext(out var chunk, cancellation))
        {
            stream.Position = chunk.Offset;
            if (chunk.Is("fmt "u8))
            {
                if (format is not null) throw new MetadataReadException("wave_complex_data");
                format = WaveFormatInfo.Read(stream, chunk.Length, chunks.BigEndian);
            }
            else if (chunk.Is("data"u8))
            {
                if (dataBytes >= 0) throw new MetadataReadException("wave_complex_data");
                dataBytes = chunk.Length;
                dataOffset = chunk.Offset;
            }
            else if (chunk.Is("LIST"u8) && chunk.Length >= 4)
            {
                stream.ReadExactly(type[..4]);
                if (type[..4].SequenceEqual("INFO"u8)) tags.ReadInfo(stream, chunk.Length - 4, chunks.BigEndian, cancellation);
                else if (type[..4].SequenceEqual("wavl"u8)) throw new MetadataReadException("wave_complex_data");
            }
            else if (chunk.Is("bext"u8)) tags.ReadBext(stream, chunk.Length);
            else if (chunk.Is("CSET"u8) && chunk.Length >= 8)
            {
                stream.ReadExactly(type); tags.CodePage = chunks.BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(type) : BinaryPrimitives.ReadUInt16LittleEndian(type);
            }
            else if (chunk.Is("id3 "u8) || chunk.Is("ID3 "u8)) throw new MetadataReadException("wave_extended_tags");
        }
        if (format is null || dataBytes < 0) throw new MetadataReadException("invalid_wave_properties");
        if (dataBytes % format.Alignment != 0) throw new MetadataReadException("invalid_wave_alignment");
        // DTS-CD/IEC61937 can masquerade as ordinary PCM. Only sniff a fixed prefix, never decode samples.
        cancellation.ThrowIfCancellationRequested();
        if (format.Bits == 16 && format.Alignment == 4 && dataBytes >= 4)
        {
            Span<byte> prefix = stackalloc byte[64]; stream.Position = dataOffset;
            var count = (int)Math.Min(dataBytes, prefix.Length); stream.ReadExactly(prefix[..count]);
            if (WavePayloadProbe.IsEncoded(prefix[..count])) throw new MetadataReadException("unsupported_wave_codec");
        }
        return MetadataRules.Normalize(file, new(tags.Text("title", "description"), tags.Text("artist", "originator"),
            tags.Text("album"), "", (double)dataBytes / format.BytesPerSecond, format.Codec, format.Rate, format.Bits,
            tags.Number("track"), Year: tags.Number("year", true), Genre: tags.Text("genre"),
            Bitrate: (int)Math.Min(format.BytesPerSecond * 8, int.MaxValue)));
    }
}
