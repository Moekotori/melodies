using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Echo.Contracts.Media;

namespace Echo.Infrastructure.Media;

// On-demand, independent decoder. Never touches the native playback process or a library database.
public sealed class AppearanceMediaDecoder(string binary) : IAppearanceMedia
{
    public static bool IsVideo(string path) => new[] { ".mp4", ".webm", ".mkv", ".mov", ".m4v", ".avi", ".gif" }.Contains(Path.GetExtension(path).ToLowerInvariant());
    private Process Start(IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var process = new Process { StartInfo = info };
        try { if (!process.Start()) throw new IOException("媒体解码器无法启动"); return process; }
        catch { process.Dispose(); throw; }
    }
    private static void Stop(Process process)
    { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } }
    private static async Task DrainErrors(Process process, CancellationToken token)
    {
        var buffer = new char[2048];
        try { while (await process.StandardError.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) > 0) { } }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (IOException) { }
    }
    public async Task ReadVideoAsync(string path, Func<VideoFrame, CancellationToken, Task> display, CancellationToken cancellation)
    {
        if (!Path.IsPathFullyQualified(path)) throw new InvalidDataException("请选择本地视频");
        // ffprobe metadata is bounded; no full-media buffering or audio output.
        var probeInfo = new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(binary)!, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-v", "error", "-protocol_whitelist", "file,pipe", "-select_streams", "v:0", "-show_entries", "stream=width,height", "-of", "json", path }) probeInfo.ArgumentList.Add(arg);
        using var probe = Process.Start(probeInfo) ?? throw new IOException("视频信息读取失败");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var cancelProbe = timeout.Token.Register(() => Stop(probe));
        var errors = DrainErrors(probe, timeout.Token);
        var metadata = new byte[8192]; var used = 0;
        while (used < metadata.Length)
        { var n = await probe.StandardOutput.BaseStream.ReadAsync(metadata.AsMemory(used), timeout.Token).ConfigureAwait(false); if (n == 0) break; used += n; }
        if (used == metadata.Length) { Stop(probe); throw new InvalidDataException("视频信息超过读取限制"); }
        await probe.WaitForExitAsync(timeout.Token).ConfigureAwait(false); await errors.ConfigureAwait(false);
        using var json = JsonDocument.Parse(metadata.AsMemory(0, used));
        if (probe.ExitCode != 0 || json.RootElement.GetProperty("streams").GetArrayLength() == 0) throw new InvalidDataException("视频无法读取，请换一个文件");
        var stream = json.RootElement.GetProperty("streams")[0]; var w = stream.GetProperty("width").GetInt32(); var h = stream.GetProperty("height").GetInt32();
        if (w <= 0 || h <= 0 || w > 8192 || h > 8192 || (long)w * h > 32 * 1024 * 1024) throw new InvalidDataException("视频分辨率过大");
        var factor = Math.Min(1, 960d / Math.Max(w, h)); w = Math.Max(2, (int)(w * factor) / 2 * 2); h = Math.Max(2, (int)(h * factor) / 2 * 2);
        using var process = Start(["-v", "error", "-nostdin", "-threads", "1", "-filter_threads", "1", "-stream_loop", "-1", "-re", "-protocol_whitelist", "file,pipe",
            "-i", path, "-an", "-sn", "-dn", "-vf", $"fps=15,scale={w}:{h}", "-pix_fmt", "bgra", "-f", "rawvideo", "pipe:1"]);
        using var kill = cancellation.Register(() => Stop(process)); var drain = DrainErrors(process, cancellation);
        try
        {
            var frame = new VideoFrame(w, h, new byte[checked(w * h * 4)]);
            while (true)
            {
                await process.StandardOutput.BaseStream.ReadExactlyAsync(frame.Bgra, cancellation).ConfigureAwait(false);
                await display(frame, cancellation).ConfigureAwait(false); // one frame and one UI acknowledgement, no Dispatcher backlog
            }
        }
        catch (EndOfStreamException) { cancellation.ThrowIfCancellationRequested(); throw new InvalidDataException("视频解码结束，请重新选择或重试"); }
        finally { Stop(process); await drain.ConfigureAwait(false); }
    }
    public async Task<TrackEnvelope> AnalyzeAsync(string path, double duration, CancellationToken cancellation)
    {
        if (!Path.IsPathFullyQualified(path) || !double.IsFinite(duration) || duration <= 0 || duration > 4 * 3600)
            throw new InvalidDataException("可视化仅支持不超过四小时的本地曲目");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        using var process = Start(["-v", "error", "-nostdin", "-threads", "1", "-filter_threads", "1", "-protocol_whitelist", "file,pipe", "-i", path,
            "-vn", "-sn", "-dn", "-ac", "1", "-ar", "4000", "-t", duration.ToString(CultureInfo.InvariantCulture), "-f", "f32le", "pipe:1"]);
        using var kill = timeout.Token.Register(() => Stop(process)); var drain = DrainErrors(process, timeout.Token);
        var peaks = new float[1024]; var sums = new double[1024]; var counts = new int[1024]; var buffer = new byte[16384];
        long sample = 0; var expected = Math.Ceiling(duration * 4000); var carry = 0;
        try
        {
            while (true)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(buffer.AsMemory(carry), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                var total = read + carry; var complete = total / 4 * 4;
                for (var i = 0; i < complete; i += 4)
                {
                    if (sample >= expected + 4000) throw new InvalidDataException("音频解码长度超过限制");
                    var value = BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(i, 4)));
                    var bucket = Math.Min(1023, (int)(sample++ / expected * 1024));
                    if (!float.IsFinite(value)) continue;
                    value = Math.Clamp(Math.Abs(value), 0, 1); peaks[bucket] = Math.Max(peaks[bucket], value); sums[bucket] += value * value; counts[bucket]++;
                }
                carry = total - complete; buffer.AsSpan(complete, carry).CopyTo(buffer);
            }
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            if (process.ExitCode != 0 || sample == 0) throw new InvalidDataException("当前曲目无法分析，已保留普通进度条");
            var energy = counts.Select((count, i) => count == 0 ? 0f : (float)Math.Sqrt(sums[i] / count)).ToArray();
            var maximum = energy.Max(); if (maximum > 0) for (var i = 0; i < energy.Length; i++) energy[i] /= maximum;
            return new(duration, peaks, energy);
        }
        finally { Stop(process); await drain.ConfigureAwait(false); }
    }
}
