using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Echo.Contracts.Library;
using Echo.Infrastructure.Metadata;

namespace Echo.Infrastructure.Library;

// Hash only the bounded command payload, never audio contents.
internal static class ScanBatchFingerprint
{
    public static string Compute(IReadOnlyList<ScanEntry> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            void Text(string? value)
            {
                value ??= "";
                if (value.Length > 4096) throw new ArgumentException("Field exceeds its bounded limit.");
                BinaryPrimitives.WriteInt32LittleEndian(buffer, value.Length);
                hash.AppendData(buffer.AsSpan(0, 4));
                var size = Encoding.UTF8.GetBytes(value, buffer.AsSpan(4));
                hash.AppendData(buffer.AsSpan(4, size));
            }
            foreach (var entry in entries)
            {
                Text(entry.File.Path); Text(entry.File.PathKey); Text(entry.ExistingId); Text(entry.ErrorCode);
                if (TrackSourceJson.Encode(entry.File.Source) is { } source)
                    foreach (var segment in source.Chunk(4096)) Text(new string(segment));
                BinaryPrimitives.WriteInt64LittleEndian(buffer, entry.File.SizeBytes);
                BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(8), entry.File.ModifiedTicks);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), (int)entry.Kind);
                hash.AppendData(buffer.AsSpan(0, 20));
                if (entry.Metadata is not { } metadata) continue;
                Text(metadata.Title); Text(metadata.Artist); Text(metadata.Album); Text(metadata.AlbumArtist);
                Text(metadata.Codec); Text(metadata.Genre);
                BinaryPrimitives.WriteDoubleLittleEndian(buffer, metadata.DurationSeconds);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(8), metadata.SampleRate);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(12), metadata.BitDepth);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), metadata.TrackNumber ?? -1);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(20), metadata.DiscNumber ?? -1);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(24), metadata.Year ?? -1);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(28), metadata.Bitrate ?? -1);
                BinaryPrimitives.WriteDoubleLittleEndian(buffer.AsSpan(32), metadata.Bpm ?? -1);
                hash.AppendData(buffer.AsSpan(0, 40));
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
