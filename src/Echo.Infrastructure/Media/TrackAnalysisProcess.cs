using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Echo.Contracts.Media;

namespace Echo.Infrastructure.Media;

// One-shot audio-engine process. It decodes in-process and returns gain, true peak
// and 1024 bins. PCM never crosses into this process, and playback is left alone.
internal static class TrackAnalysisProcess
{
    public static async Task<TrackEnvelope> RunAsync(string binary, string path, double duration, CancellationToken cancellation)
    {
        if (!Path.IsPathFullyQualified(path) || !double.IsFinite(duration) || duration <= 0 || duration > 4 * 3600)
            throw new InvalidDataException("可视化仅支持不超过四小时的本地曲目");
        if (!File.Exists(binary))
            throw new FileNotFoundException("找不到曲目分析程序", binary);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = Start(binary, path, duration);
        using var kill = timeout.Token.Register(() => Stop(process));
        var errors = Drain(process, timeout.Token);
        try
        {
            var output = new byte[96 * 1024];
            var used = 0;
            while (used < output.Length)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(output.AsMemory(used), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                used += read;
            }
            if (used == output.Length)
            {
                Stop(process);
                throw new InvalidDataException("曲目分析结果超过上限");
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 || used == 0)
                throw new InvalidDataException("当前曲目无法分析，已保留普通进度条");
            return Parse(output.AsMemory(0, used), duration);
        }
        finally
        {
            Stop(process);
            await errors.ConfigureAwait(false);
        }
    }

    private static Process Start(string binary, string path, double duration)
    {
        var info = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add(path);
        info.ArgumentList.Add(duration.ToString(CultureInfo.InvariantCulture));
        var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start()) throw new IOException("曲目分析程序无法启动");
            return process;
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }

    private static async Task Drain(Process process, CancellationToken token)
    {
        var buffer = new char[2048];
        try
        {
            while (await process.StandardError.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) > 0) { }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (IOException) { }
    }

    private static TrackEnvelope Parse(ReadOnlyMemory<byte> json, double duration)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var peaks = ReadBins(root, "peaks");
        var energy = ReadBins(root, "energy");
        var gain = root.GetProperty("gainDb").GetDouble();
        var truePeak = root.GetProperty("truePeak").GetDouble();
        if (!double.IsFinite(gain) || !double.IsFinite(truePeak) || truePeak < 0)
            throw new InvalidDataException("曲目分析结果无效");
        return new TrackEnvelope(duration, peaks, energy, gain, truePeak);
    }

    private static float[] ReadBins(JsonElement root, string name)
    {
        var array = root.GetProperty(name);
        if (array.GetArrayLength() != 1024)
            throw new InvalidDataException("曲目分析结果无效");
        var values = new float[1024];
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var value = item.GetSingle();
            if (!float.IsFinite(value))
                throw new InvalidDataException("曲目分析结果无效");
            values[index++] = Math.Clamp(value, 0, 1);
        }
        return values;
    }
}
