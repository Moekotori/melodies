using System.Buffers.Binary;
using System.Text;
using Echo.Contracts.Library;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Metadata;

internal static class WaveVariants
{
    // Actual encoded audio, scanned only. No output device is opened by this probe.
    public static async Task CheckAsync(string ffmpeg, string root, string scanner, CancellationToken token)
    {
        await using var reader = new LocalMetadataReader(scanner);
        var passed = 0;
        async Task<TrackMetadata> Read(string path, string? codec = null)
        {
            var file = new FileInfo(path);
            var data = await reader.ReadAsync(new(file.FullName, LibraryPath.Key(file.FullName), file.Length, file.LastWriteTimeUtc.Ticks), token);
            if (data.SampleRate != 48000 || data.DurationSeconds < 0.3 || data.DurationSeconds > 0.5 || data.Codec.Length == 0 ||
                codec is not null && !data.Codec.Equals(codec, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"WAV properties {file.Name}: {data.Codec}/{data.SampleRate}/{data.DurationSeconds}");
            Console.WriteLine($"PASS WAVE {file.Name}: {data.Codec}/{data.BitDepth}, duration={data.DurationSeconds:F6}");
            passed++; return data;
        }
        foreach (var encoder in new[] { "pcm_u8", "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le", "pcm_f64le",
            "adpcm_ima_wav", "adpcm_ms", "pcm_alaw", "pcm_mulaw", "libmp3lame" })
        {
            var path = Path.Combine(root, encoder + ".wav");
            await Encode(path, encoder);
            var data = await Read(path);
            if (encoder is "adpcm_ima_wav" or "adpcm_ms" or "pcm_alaw" or "pcm_mulaw" or "libmp3lame")
            {
                var expected = encoder == "libmp3lame" ? "MP3" : encoder.ToUpperInvariant();
                if (data.Codec != expected) throw new Exception("WAV compressed codec mislabeled: " + encoder);
            }
        }
        var rf64 = Path.Combine(root, "real-rf64.wav"); await Encode(rf64, "pcm_s24le", ["-rf64", "always"]);
        await Read(rf64, "PCM");
        var w64 = Path.Combine(root, "real.w64"); await Encode(w64, "pcm_s24le", muxer: "w64"); await Read(w64, "PCM_S24LE");

        var original = await File.ReadAllBytesAsync(Path.Combine(root, "pcm_s16le.wav"), token);
        var rifx = Path.Combine(root, "big-endian.wav");
        var big = (byte[])original.Clone(); "RIFX"u8.CopyTo(big); Swap32(big.AsSpan(4));
        for (var offset = 12; offset + 8 <= big.Length;)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(big.AsSpan(offset + 4)); Swap32(big.AsSpan(offset + 4));
            if (big.AsSpan(offset, 4).SequenceEqual("fmt "u8))
            {
                var format = big.AsSpan(offset + 8, size);
                Swap16(format); Swap16(format[2..]); Swap32(format[4..]); Swap32(format[8..]); Swap16(format[12..]); Swap16(format[14..]);
            }
            // INFO child sizes also use container byte order. This fixture has only UTF-8 text there.
            if (big.AsSpan(offset, 4).SequenceEqual("LIST"u8))
                for (var child = offset + 12; child + 8 <= offset + 8 + size;)
                {
                    var length = BinaryPrimitives.ReadInt32LittleEndian(big.AsSpan(child + 4)); Swap32(big.AsSpan(child + 4)); child += 8 + length + (length & 1);
                }
            offset += 8 + size + (size & 1);
        }
        await File.WriteAllBytesAsync(rifx, big, token); await Read(rifx, "PCM_BE");
        foreach (var extension in new[] { ".wave", ".bwf" })
        {
            var alias = Path.Combine(root, "alias" + extension); await File.WriteAllBytesAsync(alias, original, token); await Read(alias, "PCM");
        }

        var id3 = Path.Combine(root, "utf16-id3.wav");
        using (var output = new MemoryStream())
        {
            output.Write(original); using var writer = new BinaryWriter(output, Encoding.UTF8, true);
            var value = Encoding.Unicode.GetBytes("ID3 中文 title\0");
            var payload = new byte[10 + 1 + 2 + value.Length]; "TIT2"u8.CopyTo(payload);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4), payload.Length - 10);
            payload[10] = 1; payload[11] = 255; payload[12] = 254; value.CopyTo(payload, 13);
            byte[] header = [73, 68, 51, 3, 0, 0, 0, 0, 0, 0];
            header[8] = (byte)(payload.Length >> 7); header[9] = (byte)(payload.Length & 127);
            writer.Write("id3 "u8); writer.Write(header.Length + payload.Length); writer.Write(header); writer.Write(payload);
            if (((header.Length + payload.Length) & 1) != 0) writer.Write((byte)0);
            output.Position = 4; writer.Write((uint)(output.Length - 8));
            await File.WriteAllBytesAsync(id3, output.ToArray(), token);
        }
        var id3Data = await Read(id3, "PCM_S16LE");
        if (id3Data.Title != "ID3 中文 title") throw new Exception("WAV ID3 UTF16 title lost: " + id3Data.Title);
        // Real G.711 data with the standard extensible subtype GUID instead of the short format tag.
        var adpcm = await File.ReadAllBytesAsync(Path.Combine(root, "pcm_alaw.wav"), token);
        var extensible = Path.Combine(root, "extensible-alaw.wav");
        using (var output = new MemoryStream())
        using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
        {
            writer.Write(adpcm.AsSpan(0, 12));
            for (var offset = 12; offset + 8 <= adpcm.Length;)
            {
                var size = BinaryPrimitives.ReadInt32LittleEndian(adpcm.AsSpan(offset + 4));
                if (adpcm.AsSpan(offset, 4).SequenceEqual("fmt "u8))
                {
                    writer.Write("fmt "u8); writer.Write(40); var format = adpcm.AsSpan(offset + 8, 16).ToArray();
                    format[0] = 254; format[1] = 255; writer.Write(format); writer.Write((ushort)22);
                    writer.Write((ushort)8); writer.Write(3u); writer.Write(6u);
                    writer.Write(new byte[] { 0, 0, 0x10, 0, 0x80, 0, 0, 0xaa, 0, 0x38, 0x9b, 0x71 });
                }
                else writer.Write(adpcm.AsSpan(offset, 8 + size + (size & 1)));
                offset += 8 + size + (size & 1);
            }
            output.Position = 4; writer.Write((uint)(output.Length - 8));
            await File.WriteAllBytesAsync(extensible, output.ToArray(), token);
        }
        await Read(extensible, "PCM_ALAW");
        foreach (var (encoder, muxer, codec) in new[] { ("dca", "dts", "DTS"), ("ac3", "spdif", "AC3") })
        {
            var rawPath = Path.Combine(root, "encoded." + muxer);
            await FormatFixtures.RunAsync(ffmpeg, ["-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i",
                "anullsrc=r=48000:cl=stereo", "-t", "0.35", "-c:a", encoder, "-b:a", encoder == "dca" ? "1536k" : "448k",
                "-strict", "-2", "-f", muxer, rawPath], token);
            var encoded = await File.ReadAllBytesAsync(rawPath, token);
            var disguised = Path.Combine(root, "pcm-disguised-" + codec + ".wav");
            using (var output = File.Create(disguised))
            using (var writer = new BinaryWriter(output))
            {
                writer.Write("RIFF"u8); writer.Write(36 + encoded.Length); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((ushort)1); writer.Write((ushort)2); writer.Write(48000); writer.Write(192000);
                writer.Write((ushort)4); writer.Write((ushort)16); writer.Write("data"u8); writer.Write(encoded.Length); writer.Write(encoded);
            }
            await Read(disguised, codec);
        }
        Console.WriteLine($"WAVE_REAL_METADATA_PASSED variants={passed}");

        Task Encode(string path, string encoder, string[]? extra = null, string muxer = "wav") =>
            FormatFixtures.RunAsync(ffmpeg, ["-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo",
                "-t", "0.35", "-c:a", encoder, ..extra ?? [], "-f", muxer, path], token);
    }

    private static void Swap16(Span<byte> value) => BinaryPrimitives.WriteUInt16BigEndian(value, BinaryPrimitives.ReadUInt16LittleEndian(value));
    private static void Swap32(Span<byte> value) => BinaryPrimitives.WriteUInt32BigEndian(value, BinaryPrimitives.ReadUInt32LittleEndian(value));
}
