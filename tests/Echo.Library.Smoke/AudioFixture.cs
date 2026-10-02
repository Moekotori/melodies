using System.Buffers.Binary;
using System.Text;
using Echo.Contracts.Library;
using Echo.Infrastructure.Files;

internal static class AudioFixture
{
    public static void Wave(string path, string title, string artist = "测试 Artist")
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("RIFF"u8); writer.Write(0); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)2);
        writer.Write(48000); writer.Write(192000); writer.Write((ushort)4); writer.Write((ushort)16);
        writer.Write("data"u8); writer.Write(1920); writer.Write(new byte[1920]);
        using var info = new MemoryStream();
        using (var tags = new BinaryWriter(info, Encoding.UTF8, true))
        {
            tags.Write("INFO"u8);
            Tag(tags, "INAM", title); Tag(tags, "IART", artist); Tag(tags, "IPRD", "扫描验证 Album");
        }
        writer.Write("LIST"u8); writer.Write((int)info.Length); writer.Write(info.ToArray());
        if ((info.Length & 1) != 0) writer.Write((byte)0);
        stream.Position = 4; writer.Write((int)stream.Length - 8);
    }
    private static void Tag(BinaryWriter writer, string key, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        writer.Write(Encoding.ASCII.GetBytes(key)); writer.Write(bytes.Length); writer.Write(bytes);
        if ((bytes.Length & 1) != 0) writer.Write((byte)0);
    }
    public static void Flac(string path)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("fLaC"u8); writer.Write((byte)0); writer.Write(new byte[] { 0, 0, 34 });
        var info = new byte[34];
        BinaryPrimitives.WriteUInt16BigEndian(info, 4096);
        BinaryPrimitives.WriteUInt16BigEndian(info.AsSpan(2), 4096);
        var packed = ((ulong)48000 << 44) | ((ulong)1 << 41) | ((ulong)23 << 36) | 48000UL;
        BinaryPrimitives.WriteUInt64BigEndian(info.AsSpan(10), packed);
        writer.Write(info);
        using var comments = new MemoryStream();
        using (var tags = new BinaryWriter(comments, Encoding.UTF8, true))
        {
            tags.Write(4); tags.Write("ECHO"u8); tags.Write(4);
            foreach (var text in new[] { "TITLE=无损 测试", "ARTIST=FLAC Artist", "ALBUM=Demo", "TRACKNUMBER=2/9" })
            { var bytes = Encoding.UTF8.GetBytes(text); tags.Write(bytes.Length); tags.Write(bytes); }
        }
        writer.Write((byte)0x84);
        writer.Write(new byte[] { (byte)(comments.Length >> 16), (byte)(comments.Length >> 8), (byte)comments.Length });
        writer.Write(comments.ToArray());
        // Header/metadata fixture. No decode or listening claim.
    }
    public static FileSnapshot Snapshot(string path)
    {
        var info = new FileInfo(path);
        return new(path, LibraryPath.Key(path), info.Length, info.LastWriteTimeUtc.Ticks);
    }
}
