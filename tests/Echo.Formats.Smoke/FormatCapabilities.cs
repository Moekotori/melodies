using System.Diagnostics;
using System.Text.Json;
using Echo.Contracts.Media;

internal static class FormatCapabilities
{
    public static async Task CheckAsync(string scanner, CancellationToken token)
    {
        using var process = Process.Start(new ProcessStartInfo(scanner) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true }) ?? throw new Exception("scanner failed to start");
        try
        {
            using var json = JsonDocument.Parse(await process.StandardOutput.ReadLineAsync(token) ?? throw new Exception("no native capabilities"));
            var root = json.RootElement;
            if (!root.GetProperty("ffmpegAudioMetadata").GetBoolean()) throw new Exception("FFmpeg metadata fallback not deployed");
            var extensions = root.GetProperty("metadataExtensions").EnumerateArray().Select(x => x.GetString()).ToArray();
            if (!extensions.SequenceEqual(AudioFormatCatalog.DirectAudioExtensions)) throw new Exception("native/managed supported formats drift");
            await process.StandardOutput.ReadLineAsync(token);
            // Send a harmless missing-file request to complete the scanner's persistent protocol.
            await process.StandardInput.WriteLineAsync("{\"type\":\"metadata\",\"requestId\":\"capabilities\",\"path\":\"echo-format-capability-missing-file\",\"readCover\":false}");
            await process.StandardInput.FlushAsync(token);
            await process.StandardOutput.ReadLineAsync(token);
            process.StandardInput.Close(); await process.WaitForExitAsync(token);
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
    }
}
