using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Echo.Contracts.Library;

namespace Echo.Infrastructure.Library;

// Hash only the bounded command payload, never audio contents.
internal static class ScanBatchFingerprint
{
    public static string Compute(IReadOnlyList<ScanEntry> entries, IReadOnlyList<string?> sources)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            void Text(ReadOnlySpan<char> value)
            {
                if (value.Length > 4096) throw new ArgumentException("Field exceeds its bounded limit.");
                BinaryPrimitives.WriteInt32LittleEndian(buffer, value.Length);
                hash.AppendData(buffer.AsSpan(0, 4));
                var size = Encoding.UTF8.GetBytes(value, buffer.AsSpan(4));
                hash.AppendData(buffer.AsSpan(4, size));
            }
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                Text(entry.File.Path); Text(entry.File.PathKey); Text(entry.ExistingId); Text(entry.ErrorCode);
                if (sources[index] is { } source)
                    for (var offset = 0; offset < source.Length; offset += 4096)
                        Text(source.AsSpan(offset, Math.Min(4096, source.Length - offset)));
                BinaryPrimitives.WriteInt64LittleEndian(buffer, entry.File.SizeBytes);
                BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(8), entry.File.ModifiedTicks);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(16), (int)entry.Kind);
                hash.AppendData(buffer.AsSpan(0, 20));
                if (entry.Metadata is not { } metadata) continue;
                Text(metadata.Title); Text(metadata.Artist); Text(metadata.Album); Text(metadata.AlbumArtist);
                Text(metadata.Codec); Text(metadata.Genre);
                ArtistRoleRules.Validate(metadata.Credits);
                BinaryPrimitives.WriteInt32LittleEndian(buffer, metadata.Credits?.Count ?? 0); hash.AppendData(buffer.AsSpan(0,4));
                if (metadata.Credits is { } credits)
                    foreach (var credit in credits) { Text(credit.Tag); Text(credit.Value); }
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
