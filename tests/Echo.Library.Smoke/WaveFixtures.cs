using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

internal static partial class WaveFixtures
{
    internal sealed record Chunk(string Id, byte[] Bytes, bool Large = false);
    public static byte[] Format(int bits = 16, int channels = 2, int encoding = 1, int valid = 0)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        writer.Write((ushort)(valid > 0 ? 0xfffe : encoding)); writer.Write((ushort)channels);
        writer.Write(48000); writer.Write(48000 * channels * bits / 8);
        writer.Write((ushort)(channels * bits / 8)); writer.Write((ushort)bits);
        if (valid > 0)
        {
            writer.Write((ushort)22); writer.Write((ushort)valid); writer.Write(0u);
            writer.Write((uint)encoding); writer.Write(new byte[] { 0, 0, 0x10, 0, 0x80, 0, 0, 0xaa, 0, 0x38, 0x9b, 0x71 });
        }
        return memory.ToArray();
    }

    public static Chunk Info(params (string Id, byte[] Bytes)[] tags)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory);
        writer.Write("INFO"u8);
        foreach (var (id, bytes) in tags) WriteChunk(writer, new(id, bytes));
        return new("LIST", memory.ToArray());
    }
    public static byte[] Text(string value) => Encoding.UTF8.GetBytes(value + "\0");
    public static Chunk Bext(string title = "Broadcast title", string artist = "Studio", string date = "2003-12-31")
    {
        var bytes = new byte[602];
        Encoding.UTF8.GetBytes(title).CopyTo(bytes, 0);
        Encoding.UTF8.GetBytes(artist).CopyTo(bytes, 256);
        Encoding.UTF8.GetBytes(date).CopyTo(bytes, 320);
        return new("bext", bytes);
    }

    public static void Write(string path, string container = "RIFF", params Chunk[] chunks)
    {
        using var stream = File.Create(path); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes(container)); writer.Write(container == "RIFF" ? 0u : uint.MaxValue); writer.Write("WAVE"u8);
        if (container != "RIFF")
        {
            var table = chunks.Where(x => x.Large && x.Id != "data").ToArray();
            writer.Write("ds64"u8); writer.Write(28 + table.Length * 12);
            writer.Write(0L); writer.Write((long)(chunks.FirstOrDefault(x => x.Id == "data")?.Bytes.Length ?? 0));
            writer.Write(0L); writer.Write(table.Length);
            foreach (var chunk in table) { writer.Write(Encoding.ASCII.GetBytes(chunk.Id)); writer.Write((long)chunk.Bytes.Length); }
        }
        foreach (var chunk in chunks) WriteChunk(writer, chunk);
        stream.Position = container == "RIFF" ? 4 : 20;
        if (container == "RIFF") writer.Write((uint)(stream.Length - 8)); else writer.Write(stream.Length - 8);
    }

    public static void Sparse(string path, long dataLength)
    {
        using var stream = File.Create(path);
        if (OperatingSystem.IsWindows() && !DeviceIoControl(stream.SafeFileHandle, 0x900c4,
            IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero)) throw new IOException("Cannot enable sparse WAV fixture");
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        var info = Info(("INAM", Text("After 4 GiB")));
        var total = 12L + 36 + 24 + 8 + dataLength + 8 + info.Bytes.Length + (info.Bytes.Length & 1);
        writer.Write("RF64"u8); writer.Write(uint.MaxValue); writer.Write("WAVE"u8);
        writer.Write("ds64"u8); writer.Write(28); writer.Write(total - 8); writer.Write(dataLength); writer.Write(dataLength / 4); writer.Write(0);
        WriteChunk(writer, new("fmt ", Format())); writer.Write("data"u8); writer.Write(uint.MaxValue);
        stream.Position += dataLength; WriteChunk(writer, info);
    }

    private static void WriteChunk(BinaryWriter writer, Chunk chunk)
    {
        writer.Write(Encoding.ASCII.GetBytes(chunk.Id)); writer.Write(chunk.Large ? uint.MaxValue : (uint)chunk.Bytes.Length);
        writer.Write(chunk.Bytes); if ((chunk.Bytes.Length & 1) != 0) writer.Write((byte)0);
    }

    public static void Patch(string path, long offset, uint value)
    {
        using var stream = File.OpenWrite(path); stream.Position = offset;
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); stream.Write(bytes);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
