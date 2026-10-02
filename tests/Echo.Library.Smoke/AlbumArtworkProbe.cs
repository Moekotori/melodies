using System.Buffers.Binary;
using System.Text;
using Echo.Infrastructure.Artwork;

internal static class AlbumArtworkProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        Directory.CreateDirectory(root);
        var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a3ZsAAAAASUVORK5CYII=");
        var source = new LocalAlbumArtwork();
        var flac = Path.Combine(root, "sample.flac");
        using (var stream = File.Create(flac))
        {
            using var body = new MemoryStream();
            WriteBig(body, 3); WriteBig(body, 9); body.Write("image/png"u8); WriteBig(body, 0);
            WriteBig(body, 1); WriteBig(body, 1); WriteBig(body, 32); WriteBig(body, 0); WriteBig(body, image.Length); body.Write(image);
            stream.Write("fLaC"u8); stream.WriteByte(0x86);
            stream.WriteByte((byte)(body.Length >> 16)); stream.WriteByte((byte)(body.Length >> 8)); stream.WriteByte((byte)body.Length);
            body.Position = 0; body.CopyTo(stream);
        }
        Require((await source.ReadAsync(flac, cancellation))!.SequenceEqual(image), "FLAC picture block");
        foreach (var version in new[] { 3, 4 })
        {
            var mp3 = Path.Combine(root, $"sample-{version}.mp3");
            using var apic = new MemoryStream();
            apic.WriteByte(0); apic.Write(Encoding.ASCII.GetBytes("image/png\0")); apic.WriteByte(3); apic.WriteByte(0); apic.Write(image);
            using (var stream = File.Create(mp3))
            {
                stream.Write("ID3"u8); stream.WriteByte((byte)version); stream.WriteByte(0); stream.WriteByte(0);
                WriteSync(stream, (int)apic.Length + 10);
                stream.Write("APIC"u8);
                if (version == 4) WriteSync(stream, (int)apic.Length); else WriteBig(stream, (int)apic.Length);
                stream.WriteByte(0); stream.WriteByte(0); apic.Position = 0; apic.CopyTo(stream);
            }
            Require((await source.ReadAsync(mp3, cancellation))!.SequenceEqual(image), "ID3 APIC v" + version);
        }
        await File.WriteAllBytesAsync(Path.Combine(root, "cover.png"), image, cancellation);
        Require((await source.ReadAsync(Path.Combine(root, "missing.wav"), cancellation))!.SequenceEqual(image), "directory cover");
        using (var oversized = File.OpenWrite(Path.Combine(root, "cover.png"))) oversized.SetLength(4 * 1024 * 1024 + 1);
        Require(await source.ReadAsync(Path.Combine(root, "missing.wav"), cancellation) is null, "encoded byte cap");
        await File.WriteAllBytesAsync(Path.Combine(root, "broken.flac"), "fLaC"u8.ToArray(), cancellation);
        Require(await source.ReadAsync(Path.Combine(root, "broken.flac"), cancellation) is null, "truncated picture fallback");
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        try { await source.ReadAsync(flac, stopped.Token); throw new Exception("Art cancellation ignored"); }
        catch (OperationCanceledException) { }
        Console.WriteLine("Artwork checks passed: directory, FLAC, ID3 v2.3/v2.4, size cap, malformed fallback and cancellation.");
    }
    private static void WriteBig(Stream stream, int value)
    { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); stream.Write(bytes); }
    private static void WriteSync(Stream stream, int value)
    { stream.Write([(byte)((value >> 21) & 127), (byte)((value >> 14) & 127), (byte)((value >> 7) & 127), (byte)(value & 127)]); }
    private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
}
