using System.Buffers.Binary;
using System.Globalization;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

// Retain only bounded relevant fields; decode after traversal so a late CSET applies to INFO/bext.
internal sealed class WaveTags
{
    private readonly Dictionary<string, (int Priority, byte[] Bytes)> _fields = new(StringComparer.Ordinal);
    private int _tags, _bytes;
    public int CodePage { get; set; }

    public void ReadInfo(Stream stream, long length, bool bigEndian, CancellationToken token)
    {
        var end = stream.Position + length;
        Span<byte> header = stackalloc byte[8];
        while (stream.Position < end)
        {
            token.ThrowIfCancellationRequested();
            if (++_tags > 256) throw new MetadataReadException("wave_tag_limit");
            if (end - stream.Position < 8) throw new MetadataReadException("truncated_wave_tag");
            stream.ReadExactly(header);
            var size = bigEndian ? BinaryPrimitives.ReadUInt32BigEndian(header[4..]) : BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
            if ((long)size + (size & 1) > end - stream.Position) throw new MetadataReadException("truncated_wave_tag");
            var next = stream.Position + size + (size & 1);
            var (field, priority) = Field(header[..4]);
            if (field is not null)
            {
                if (size > 16 * 1024) throw new MetadataReadException("wave_tag_size_limit");
                Charge((int)size);
                if (!_fields.TryGetValue(field, out var previous) || previous.Priority <= priority)
                {
                    var bytes = new byte[(int)size]; stream.ReadExactly(bytes);
                    _fields[field] = (priority, bytes);
                }
            }
            stream.Position = next;
        }
    }

    public void ReadBext(Stream stream, long length)
    {
        Span<byte> prefix = stackalloc byte[602];
        var count = (int)Math.Min(length, prefix.Length);
        Charge(count); stream.ReadExactly(prefix[..count]);
        KeepBext("description", prefix[..count], 0, 256);
        KeepBext("originator", prefix[..count], 256, 32);
        KeepBext("date", prefix[..count], 320, 10);
    }

    public string Text(string field, string? fallback = null)
    {
        var value = _fields.TryGetValue(field, out var entry) ? WaveTextDecoder.Decode(entry.Bytes, CodePage) : "";
        return value.Length == 0 && fallback is not null ? Text(fallback) : value;
    }

    public int? Number(string field, bool year = false)
    {
        var value = Text(field, year ? "date" : null).AsSpan();
        if (year) value = value[..Math.Min(4, value.Length)];
        else if (value.IndexOf('/') is var slash && slash >= 0) value = value[..slash];
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private void KeepBext(string key, ReadOnlySpan<byte> prefix, int offset, int count)
    {
        if (prefix.Length >= offset + count) _fields[key] = (0, prefix.Slice(offset, count).ToArray());
    }

    private void Charge(int size)
    {
        _bytes += size;
        if (_bytes > 256 * 1024) throw new MetadataReadException("wave_tag_size_limit");
    }

    private static (string? Field, int Priority) Field(ReadOnlySpan<byte> id)
    {
        if (id.SequenceEqual("INAM"u8)) return ("title", 2);
        if (id.SequenceEqual("TITL"u8)) return ("title", 1);
        if (id.SequenceEqual("IART"u8)) return ("artist", 2);
        if (id.SequenceEqual("IPRD"u8)) return ("album", 2);
        if (id.SequenceEqual("IRPD"u8)) return ("album", 1);
        if (id.SequenceEqual("IGNR"u8)) return ("genre", 2);
        if (id.SequenceEqual("GNRE"u8)) return ("genre", 1);
        if (id.SequenceEqual("ICRD"u8)) return ("year", 2);
        if (id.SequenceEqual("YEAR"u8)) return ("year", 1);
        if (id.SequenceEqual("ITRK"u8)) return ("track", 2);
        if (id.SequenceEqual("IPRT"u8)) return ("track", 1);
        return (null, 0);
    }
}
