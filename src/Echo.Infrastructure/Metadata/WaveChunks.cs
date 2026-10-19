using System.Buffers.Binary;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal readonly record struct WaveChunk(uint Id, long Offset, long Length)
{
    public bool Is(ReadOnlySpan<byte> name) => Id == BinaryPrimitives.ReadUInt32LittleEndian(name);
}

// Owns chunk traversal only; the caller owns/disposes the stream. PCM/pictures are always sought over.
internal sealed class WaveChunks
{
    private readonly Stream _stream;
    private readonly bool _large;
    private long _end, _next = 12, _dataSize;
    private bool _hasDs64, _usedDataSize;
    private int _chunks;
    private (uint Id, long Size)[] _sizes = [];
    public bool BigEndian { get; }

    public WaveChunks(Stream stream)
    {
        _stream = stream;
        Span<byte> header = stackalloc byte[12]; stream.ReadExactly(header);
        BigEndian = header[..4].SequenceEqual("RIFX"u8);
        _large = header[..4].SequenceEqual("RF64"u8) || header[..4].SequenceEqual("BW64"u8);
        if ((!header[..4].SequenceEqual("RIFF"u8) && !_large && !BigEndian) || !header[8..].SequenceEqual("WAVE"u8))
            throw new MetadataReadException("unsupported_wave_container");
        var size = BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(header[4..8]) : BinaryPrimitives.ReadUInt32LittleEndian(header[4..8]);
        if (_large && size != uint.MaxValue) throw new MetadataReadException("invalid_wave_ds64");
        if (!_large && size == uint.MaxValue) throw new MetadataReadException("unsupported_wave_container");
        _end = _large ? stream.Length : 8L + size;
        if (_end < 12 || _end > stream.Length) throw new MetadataReadException("truncated_wave_container");
    }

    public bool MoveNext(out WaveChunk chunk, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        chunk = default;
        if (_next == _end) return false;
        if (_next > _end - 8) throw new MetadataReadException("truncated_wave_chunk");
        if (++_chunks > 1024) throw new MetadataReadException("wave_chunk_limit");
        _stream.Position = _next;
        Span<byte> header = stackalloc byte[8]; _stream.ReadExactly(header);
        var id = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var declared = BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(header[4..]) : BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        var isData = id == BinaryPrimitives.ReadUInt32LittleEndian("data"u8);
        long length;
        if (declared != uint.MaxValue) length = declared;
        else if (_hasDs64 && isData && !_usedDataSize) length = _dataSize;
        else
        {
            var index = -1;
            for (var i = 0; i < _sizes.Length; i++) if (_sizes[i].Id == id) { index = i; break; }
            if (!_hasDs64 || index < 0) throw new MetadataReadException("invalid_wave_ds64");
            length = _sizes[index].Size; _sizes[index].Id = 0;
        }
        if (isData) _usedDataSize = true;
        var start = _stream.Position;
        if (length > _end - start) throw new MetadataReadException("truncated_wave_chunk");
        var end = start + length;
        _next = end + (length & 1);
        if (_next > _end) throw new MetadataReadException("truncated_wave_padding");
        chunk = new(id, start, length);
        if (_large && _chunks == 1 && !chunk.Is("ds64"u8)) throw new MetadataReadException("invalid_wave_ds64");
        if (chunk.Is("ds64"u8) && _large) ReadDs64(chunk, token);
        return true;
    }

    private void ReadDs64(WaveChunk chunk, CancellationToken token)
    {
        if (_hasDs64 || chunk.Length < 28 || chunk.Length > 28 + 12 * 1024) throw new MetadataReadException("invalid_wave_ds64");
        Span<byte> data = stackalloc byte[28]; _stream.ReadExactly(data);
        var riffSize = BinaryPrimitives.ReadUInt64LittleEndian(data);
        var dataSize = BinaryPrimitives.ReadUInt64LittleEndian(data[8..]);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(data[24..]);
        if (riffSize < 4 || riffSize > (ulong)(_stream.Length - 8) || dataSize > long.MaxValue ||
            count > 1024 || 28L + count * 12L > chunk.Length) throw new MetadataReadException("invalid_wave_ds64");
        _end = (long)riffSize + 8; _dataSize = (long)dataSize;
        if (_next > _end) throw new MetadataReadException("invalid_wave_ds64");
        _sizes = new (uint, long)[count];
        Span<byte> entry = stackalloc byte[12];
        for (var i = 0; i < count; i++)
        {
            token.ThrowIfCancellationRequested(); _stream.ReadExactly(entry);
            var size = BinaryPrimitives.ReadUInt64LittleEndian(entry[4..]);
            if (size > long.MaxValue) throw new MetadataReadException("invalid_wave_ds64");
            _sizes[i] = (BinaryPrimitives.ReadUInt32LittleEndian(entry), (long)size);
        }
        _hasDs64 = true;
    }
}
