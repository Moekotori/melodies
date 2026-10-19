using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Echo.Playback.NativeAudio;

internal static class NativeOutputDiscovery
{
    public static async Task<IReadOnlyList<AudioOutputDevice>> DevicesAsync(string executable, string mode, CancellationToken token)
    {
        if (mode is not ("shared" or "exclusive" or "asio" or "ks")) throw new ArgumentException("无效的输出模式");
        string[] arguments = mode == "shared" ? ["-list"] : ["-list", "-" + mode];
        var text = await RunAsync(executable, arguments, token).ConfigureAwait(false);
        var devices = new List<AudioOutputDevice>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length < 8 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) continue;
            if (devices.Count >= 256) throw new IOException("输出设备数量超过上限");
            int.TryParse(fields[2], out var rate);
            var rates = fields.Length > 8 ? fields[8].Split(',').Select(x => int.TryParse(x, out var n) ? n : 0).Where(x => x > 0).ToArray() : [];
            devices.Add(new(fields[7], fields[1], mode, index, fields[3] == "1", rate, rates));
        }
        return devices.AsReadOnly();
    }

    public static async Task<JsonElement> ProbeAsync(string executable, AudioOutputDevice device, CancellationToken token)
    {
        if (device.Mode != "asio") throw new ArgumentException("需要选择 ASIO 驱动");
        var output = await RunAsync(executable, ["--probe-asio-sdm-families", "-device-index", device.Index.ToString(CultureInfo.InvariantCulture),
            "-device", device.Name], token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }

    internal static async Task<string> RunAsync(string executable, string[] arguments, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(8));
        var path = Path.GetFullPath(executable);
        var start = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(path)!, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("无法枚举原生输出设备");
        var output = new StringBuilder();
        var stdout = NativeJsonLines.ReadAsync(process.StandardOutput.BaseStream, bytes =>
        {
            var line = Encoding.UTF8.GetString(bytes.Span);
            if (output.Length + line.Length > 1024 * 1024) throw new IOException("输出枚举响应过大");
            output.AppendLine(line);
        }, limit.Token);
        var stderr = NativeJsonLines.DrainAsync(process.StandardError.BaseStream, limit.Token);
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(limit.Token), stdout, stderr).ConfigureAwait(false);
            if (process.ExitCode != 0) throw new IOException("原生驱动枚举/探测失败；驱动可能未安装、未连接或正在使用");
            return output.ToString();
        }
        catch
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().ConfigureAwait(false); }
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); } catch (Exception) { }
            throw;
        }
    }
}
