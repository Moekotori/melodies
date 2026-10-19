using System.Buffers.Binary;
using System.Text;

internal static class CueFixtures
{
    public static void Wave(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        const int body = 48000 * 4 * 4;
        writer.Write("RIFF"u8); writer.Write(body + 36); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(48000); writer.Write(192000);
        writer.Write((ushort)4); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(body);
        writer.BaseStream.SetLength(body + 44);
    }

    public static void Flac(string path, string? text = null, bool invalid = false, bool cd = false)
    {
        var rate = cd ? 44100 : 48000;
        var info = new byte[34];
        BinaryPrimitives.WriteUInt16BigEndian(info, 4096);
        BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(2), 4096);
        BinaryPrimitives.WriteUInt64BigEndian(info.AsSpan(10), ((ulong)(uint)rate << 44) | (1UL << 41) | (15UL << 36) | ((ulong)(uint)rate * 4));
        using var binary = new MemoryStream();
        var header = new byte[396]; header[395] = 3;
        if (cd) header[136] = 128;
        binary.Write(header);
        void Track(byte number, ulong offset, params (byte Number, ulong Offset)[] indices)
        {
            var bytes = new byte[36 + indices.Length * 12];
            BinaryPrimitives.WriteUInt64BigEndian(bytes, offset); bytes[8] = number; bytes[35] = (byte)indices.Length;
            for (var i = 0; i < indices.Length; i++)
            {
                BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(36 + i * 12), indices[i].Offset);
                bytes[44 + i * 12] = indices[i].Number;
            }
            binary.Write(bytes);
        }
        Track(1, 0, (1, 0));
        Track(invalid ? (byte)1 : (byte)2, (ulong)rate, (0, 0), (1, (ulong)rate));
        Track(cd ? (byte)170 : (byte)255, (ulong)rate * 4);
        using var comments = new MemoryStream();
        using (var tags = new BinaryWriter(comments, Encoding.UTF8, true))
        {
            var values = text is null ? new[] { "TITLE=Physical", "ARTIST=Artist" }
                : new[] { "TITLE=Physical", "CUESHEET=" + text };
            tags.Write(0); tags.Write(values.Length);
            foreach (var value in values) { var bytes = Encoding.UTF8.GetBytes(value); tags.Write(bytes.Length); tags.Write(bytes); }
        }
        using var file = File.Create(path);
        file.Write("fLaC"u8);
        void Block(byte type, byte[] bytes)
        {
            file.Write([type, (byte)(bytes.Length >> 16), (byte)(bytes.Length >> 8), (byte)bytes.Length]);
            file.Write(bytes);
        }
        Block(0, info); Block(4, comments.ToArray()); Block(133, binary.ToArray());
        // Metadata fixture only; no decodable frames or audio-device claim.
    }
}
