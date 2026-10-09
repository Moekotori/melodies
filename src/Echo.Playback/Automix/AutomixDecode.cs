using System.Diagnostics;

namespace Echo.Playback;

internal static class AutomixDecode
{
    internal const int SampleRate = 11025;
    private const double WindowSeconds = 36;

    internal static async Task<float[]> ReadAsync(string path, double startSeconds, double durationSeconds, CancellationToken token)
    {
        var ffmpeg = ResolveFfmpeg() ?? throw new InvalidOperationException("ffmpeg_missing");
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };
        if (startSeconds > 0) args.AddRange(["-ss", AutomixMath.RoundMillis(startSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        args.AddRange(["-i", path, "-vn", "-t",
            AutomixMath.RoundMillis(durationSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-f", "s16le", "-ac", "2", "-ar", SampleRate.ToString(System.Globalization.CultureInfo.InvariantCulture), "pipe:1"]);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new InvalidOperationException("ffmpeg_start_failed");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var registration = timeout.Token.Register(() => { try { process.Kill(true); } catch (Exception) { } });
        var drain = process.StandardError.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
        var expected = SampleRate * 2 * 2 * (int)WindowSeconds;
        var bytes = await ReadCappedAsync(process.StandardOutput.BaseStream, expected + 8192, timeout.Token).ConfigureAwait(false);
        await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        await drain.ConfigureAwait(false);
        if (token.IsCancellationRequested) throw new OperationCanceledException(token);
        if (bytes.Length > expected) bytes = bytes[..expected];
        if (bytes.Length < SampleRate * 4 && process.ExitCode != 0) throw new InvalidOperationException("ffmpeg_exit_" + process.ExitCode);
        var samples = new float[bytes.Length / 2];
        for (var index = 0; index < samples.Length; index++)
            samples[index] = BitConverter.ToInt16(bytes, index * 2) / 32768f;
        if (samples.Length < SampleRate * 2) throw new InvalidOperationException("automix_analysis_too_short");
        return samples;
    }

    private static async Task<byte[]> ReadCappedAsync(Stream stream, int maximum, CancellationToken token)
    {
        var buffer = new byte[maximum];
        var filled = 0;
        while (filled < maximum)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(filled, maximum - filled), token).ConfigureAwait(false);
            if (read == 0) break;
            filled += read;
        }
        if (filled == maximum) { try { stream.Close(); } catch (Exception) { } }
        return filled == buffer.Length ? buffer : buffer[..filled];
    }

    private static string? ResolveFfmpeg()
    {
        foreach (var variable in new[] { "ECHO_AUTOMIX_FFMPEG", "ECHO_APPEARANCE_FFMPEG" })
        {
            var configured = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        }
        var bundled = Path.Combine(AppContext.BaseDirectory, "media", OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        if (File.Exists(bundled)) return bundled;
        var name = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
