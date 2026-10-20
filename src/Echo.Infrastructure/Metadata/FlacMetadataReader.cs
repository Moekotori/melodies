using System.Buffers.Binary;
using System.Text;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

namespace Echo.Infrastructure.Metadata;

internal static class FlacMetadataReader
{
    private const int CommentLimit = 256 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    public static TrackMetadata Read(FileSnapshot file, CancellationToken cancellation)
    {
        using var stream = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            4096, FileOptions.RandomAccess);
        Span<byte> header = stackalloc byte[4];
        Span<byte> info = stackalloc byte[34];
        stream.ReadExactly(header);
        if (!header.SequenceEqual("fLaC"u8)) throw new MetadataReadException("unsupported_flac_container");
        int sampleRate = 0, bits = 0;
        long samples = 0;
        var title = ""; var artist = ""; var album = ""; var albumArtist = ""; var genre = "";
        var credits = new List<CreditTag>();
        int? track = null, disc = null, year = null;
        double? bpm = null;
        var blocks = 0;
        long metadataBytes = 0;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            if (++blocks > 128) throw new MetadataReadException("flac_block_limit");
            stream.ReadExactly(header);
            var last = (header[0] & 0x80) != 0;
            var type = header[0] & 0x7f;
            var length = (header[1] << 16) | (header[2] << 8) | header[3];
            metadataBytes += length;
            var end = stream.Position + length;
            if (end > stream.Length || metadataBytes > 64 * 1024 * 1024)
                throw new MetadataReadException("invalid_flac_metadata");
            if (type == 0)
            {
                if (blocks != 1 || length != 34) throw new MetadataReadException("invalid_flac_streaminfo");
                stream.ReadExactly(info);
                var packed = BinaryPrimitives.ReadUInt64BigEndian(info[10..18]);
                sampleRate = (int)(packed >> 44);
                bits = (int)((packed >> 36) & 0x1f) + 1;
                samples = (long)(packed & 0xfffffffff);
            }
            else if (type == 5) { } // Discovery owns textual/binary CUESHEET expansion; this reader only supplies audio metadata.
            else if (type == 4)
            {
                if (length > CommentLimit) throw new MetadataReadException("flac_comment_size_limit");
                var data = new byte[length];
                stream.ReadExactly(data);
                var offset = 0;
                var vendor = ReadLength(data, ref offset);
                Skip(data, ref offset, vendor);
                var count = ReadLength(data, ref offset);
                if (count > 512) throw new MetadataReadException("flac_comment_count_limit");
                for (var i = 0; i < count; i++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var size = ReadLength(data, ref offset);
                    if (size > data.Length - offset) throw new MetadataReadException("truncated_flac_comment");
                    var comment = data.AsSpan(offset, size);
                    offset += size;
                    var separator = comment.IndexOf((byte)'=');
                    if (separator < 0) continue;
                    var key = Encoding.ASCII.GetString(comment[..separator]);
                    var value = comment[(separator + 1)..];
                    if (Echo.Infrastructure.Library.ArtistRoleRules.Recognized(key))
                    {
                        credits.Add(new(key, Decode(value)));
                        MetadataRules.ValidateCredits(credits);
                    }
                    if (key.Equals("CUESHEET", StringComparison.OrdinalIgnoreCase)) continue;
                    if (key.Equals("TITLE", StringComparison.OrdinalIgnoreCase)) title = Decode(value);
                    else if (key.Equals("ARTIST", StringComparison.OrdinalIgnoreCase)) artist = Merge(artist, Decode(value));
                    else if (key.Equals("ALBUM", StringComparison.OrdinalIgnoreCase)) album = Decode(value);
                    else if (key.Equals("ALBUMARTIST", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("ALBUM_ARTIST", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("ALBUM ARTIST", StringComparison.OrdinalIgnoreCase)) albumArtist = Merge(albumArtist, Decode(value));
                    else if (key.Equals("GENRE", StringComparison.OrdinalIgnoreCase)) genre = Merge(genre, Decode(value));
                    else if ((key.Equals("BPM", StringComparison.OrdinalIgnoreCase) || key.Equals("TEMPO", StringComparison.OrdinalIgnoreCase))
                        && double.TryParse(Decode(value), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var tempo)) bpm = tempo;
                    else if (key.Equals("TRACKNUMBER", StringComparison.OrdinalIgnoreCase)) track = Number(value);
                    else if (key.Equals("DISCNUMBER", StringComparison.OrdinalIgnoreCase)) disc = Number(value);
                    else if (key.Equals("DATE", StringComparison.OrdinalIgnoreCase) || key.Equals("YEAR", StringComparison.OrdinalIgnoreCase))
                        year = Number(value[..Math.Min(4, value.Length)]);
                }
            }
            // PICTURE and padding are never decoded or copied.
            stream.Position = end;
            if (last) break;
        }
        if (sampleRate <= 0 || samples <= 0) throw new MetadataReadException("invalid_flac_properties");
        return MetadataRules.Normalize(file, new(title, artist, album, albumArtist, (double)samples / sampleRate,
            "FLAC", sampleRate, bits, track, disc, year, genre,
            (int)Math.Clamp(Math.Round((stream.Length - stream.Position) * 8.0 * sampleRate / samples), 0, int.MaxValue), bpm, credits));
    }

    private static int ReadLength(byte[] data, ref int offset)
    {
        if (data.Length - offset < 4) throw new MetadataReadException("truncated_flac_comment");
        var length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
        offset += 4;
        if (length > int.MaxValue) throw new MetadataReadException("invalid_flac_comment");
        return (int)length;
    }
    private static void Skip(byte[] data, ref int offset, int length)
    {
        if (length > data.Length - offset) throw new MetadataReadException("truncated_flac_comment");
        offset += length;
    }
    private static int? Number(ReadOnlySpan<byte> value)
    {
        var slash = value.IndexOf((byte)'/');
        if (slash >= 0) value = value[..slash];
        return int.TryParse(Decode(value), out var number) ? number : null;
    }
    private static string Decode(ReadOnlySpan<byte> value)
    {
        try { return Utf8.GetString(value); }
        catch (DecoderFallbackException) { throw new MetadataReadException("flac_tag_encoding"); }
    }
    private static string Merge(string existing, string value) =>
        existing.Length == 0 ? value : existing == value ? existing : existing + "; " + value;
}
