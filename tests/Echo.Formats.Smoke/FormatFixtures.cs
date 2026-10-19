using System.Diagnostics;
using System.Text;

internal static class FormatFixtures
{
    internal sealed record Fixture(string Name, string Path);
    public static async Task<IReadOnlyList<Fixture>> CreateAsync(string ffmpeg, string root, CancellationToken token, bool adpcmOnly = false)
    {
        Directory.CreateDirectory(root);
        (string name, string extension, string encoder, string muxer)[] definitions =
        [
            ("WAV", "wav", "pcm_s16le", "wav"), ("FLAC", "flac", "flac", "flac"),
            ("MP3", "mp3", "libmp3lame", "mp3"), ("AAC", "aac", "aac", "adts"),
            ("AAC/M4A", "m4a", "aac", "ipod"), ("ALAC", "alac", "alac", "ipod"),
            ("Vorbis/OGA", "oga", "libvorbis", "ogg"), ("Opus", "opus", "libopus", "opus"),
            ("WavPack", "wv", "wavpack", "wv"), ("TTA", "tta", "tta", "tta"),
            ("AIFF", "aiff", "pcm_s16be", "aiff"), ("AIFC", "aifc", "pcm_f32be", "aiff"),
            ("AU", "au", "pcm_s16be", "au"), ("VOC", "voc", "pcm_u8", "voc"),
            ("W64", "w64", "pcm_s24le", "w64"), ("CAF", "caf", "pcm_s16le", "caf"),
            ("WMA", "wma", "wmav2", "asf"), ("MP2", "mp2", "mp2", "mp2"),
            ("AC3", "ac3", "ac3", "ac3"), ("EAC3", "eac3", "eac3", "eac3"),
            ("TrueHD", "truehd", "truehd", "truehd"), ("DTS", "dts", "dca", "dts"),
            ("MKA/FLAC", "mka", "flac", "matroska"), ("MKV/Opus", "mkv", "libopus", "matroska"),
            ("WebM/Opus", "webm", "libopus", "webm"), ("MOV/AAC", "mov", "aac", "mov"), ("MP4/AAC", "mp4", "aac", "mp4"),
            ("WAV/ADPCM", "wav", "adpcm_ima_wav", "wav")
        ];
        var fixtures = new List<Fixture>();
        var sequence = 0;
        foreach (var definition in adpcmOnly ? definitions.Where(x => x.encoder == "adpcm_ima_wav") : definitions)
        {
            var path = System.IO.Path.Combine(root, (++sequence).ToString("D2") + "." + definition.extension);
            var signal = definition.encoder == "truehd" ? "sine=frequency=440:sample_rate=48000" : "anullsrc=r=48000:cl=stereo";
            var seconds = definition.encoder == "truehd" ? "1" : "0.35";
            await RunAsync(ffmpeg, ["-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", signal,
                "-t", seconds, "-ac", "2", "-c:a", definition.encoder, "-strict", "-2", "-metadata", "title=ECHO format fixture", "-f", definition.muxer, path], token);
            fixtures.Add(new(definition.name, path));
        }
        if (!adpcmOnly)
        {
            var dsf = System.IO.Path.Combine(root, "28.dsf"); WriteDsf(dsf); fixtures.Add(new("DSF/DSD64", dsf));
            var rf64 = System.IO.Path.Combine(root, "29.wav");
            await RunAsync(ffmpeg, ["-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", "anullsrc=r=48000:cl=stereo", "-t", "0.35", "-c:a", "pcm_s16le", "-rf64", "always", rf64], token);
            fixtures.Add(new("RF64", rf64));
        }
        var video = System.IO.Path.Combine(root, "video-only.webm");
        await RunAsync(ffmpeg, ["-hide_banner", "-v", "error", "-y", "-f", "lavfi", "-i", "color=black:s=16x16:r=10", "-t", "0.2", "-an", "-c:v", "libvpx-vp9", video], token);
        return fixtures;
    }
    internal static async Task RunAsync(string executable, string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new Exception("fixture encoder failed to start");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(3));
        var error = process.StandardError.ReadToEndAsync(deadline.Token);
        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            if (process.ExitCode != 0) throw new Exception("fixture encoder failed: " + await error);
            await stdout;
        }
        catch { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } throw; }
    }
    private static void WriteDsf(string path)
    {
        const int sampleRate = 2822400, block = 4096;
        const long samples = sampleRate * 35L / 100;
        var blocks = (int)((samples / 8 + block - 1) / block);
        var body = blocks * block * 2;
        using var writer = new BinaryWriter(File.Create(path), Encoding.UTF8);
        writer.Write("DSD "u8); writer.Write(28L); writer.Write(28L + 52L + 12L + body); writer.Write(0L);
        writer.Write("fmt "u8); writer.Write(52L); writer.Write(1); writer.Write(0); writer.Write(2); writer.Write(2);
        writer.Write(sampleRate); writer.Write(1); writer.Write(samples); writer.Write(block); writer.Write(0);
        writer.Write("data"u8); writer.Write(12L + body); writer.Write(Enumerable.Repeat((byte)0x69, body).ToArray());
    }
}
