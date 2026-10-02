using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;

namespace Echo.Infrastructure.Metadata;

// One worker owns one persistent C++ process. Covers and PCM never cross this boundary.
public sealed class NativeMetadataReader(string scannerPath) : IMetadataReader
{
    private Process? _process;
    private BoundedLineReader? _lines;
    private long _sequence;
    private int _consecutiveTransportFailures;
    private static readonly NativeRequestContext Json = new(new JsonSerializerOptions
    { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    public async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken cancellation)
        => await ReadAsync(file, cancellation, false).ConfigureAwait(false);
    internal async ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken cancellation, bool allowUntaggedMp3)
    {
        if (_consecutiveTransportFailures >= 3) throw new MetadataReadException("native_circuit_open");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            if (_process is null) await StartAsync(deadline.Token).ConfigureAwait(false);
            var process = _process!;
            using var stop = deadline.Token.Register(() => Kill(process));
            var id = (++_sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var message = JsonSerializer.Serialize(new NativeRequest("metadata", id, file.Path, false, allowUntaggedMp3), Json.NativeRequest);
            await process.StandardInput.WriteLineAsync(message.AsMemory(), deadline.Token).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(deadline.Token).ConfigureAwait(false);
            using var parsed = JsonDocument.Parse(await _lines!.ReadAsync(deadline.Token).ConfigureAwait(false));
            var root = parsed.RootElement;
            if (Text(root, "requestId") != id) throw new MetadataReadException("native_request_mismatch");
            if (Text(root, "type") is "metadata" or "unsupported" or "error") _consecutiveTransportFailures = 0;
            if (Text(root, "type") == "unsupported") throw new MetadataReadException("native_format_unsupported");
            if (Text(root, "type") != "metadata") throw new MetadataReadException("native_metadata_failed");
            var fields = root.GetProperty("result").GetProperty("fields");
            _consecutiveTransportFailures = 0;
            var value = new TrackMetadata(Text(fields, "title"), Text(fields, "artist"), Text(fields, "album"),
                Text(fields, "albumArtist"), Double(fields, "duration"), Text(fields, "codec"),
                Integer(fields, "sampleRate") ?? 0, Integer(fields, "bitDepth") ?? 0,
                Integer(fields, "trackNo"), Integer(fields, "discNo"), Integer(fields, "year"), Text(fields, "genre"));
            return MetadataRules.Normalize(file, value);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            if (error is MetadataReadException
                { Code: "native_format_unsupported" or "native_metadata_failed" or "invalid_audio_properties" or "metadata_text_limit" })
                throw;
            _consecutiveTransportFailures++;
            await DisposeAsync().ConfigureAwait(false);
            if (cancellation.IsCancellationRequested) throw new OperationCanceledException(cancellation);
            if (deadline.IsCancellationRequested) throw new MetadataReadException("native_metadata_timeout");
            if (error is MetadataReadException) throw;
            throw new MetadataReadException("native_metadata_protocol");
        }
    }

    private async Task StartAsync(CancellationToken cancellation)
    {
        var start = new ProcessStartInfo(LibraryPath.Canonical(scannerPath))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = new UTF8Encoding(false)
        };
        var process = Process.Start(start) ?? throw new MetadataReadException("native_scanner_unavailable");
        _process = process;
        _lines = new(process.StandardOutput);
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or PlatformNotSupportedException) { }
        using var stop = cancellation.Register(() => Kill(process));
        using var capabilities = JsonDocument.Parse(await _lines.ReadAsync(cancellation).ConfigureAwait(false));
        using var ready = JsonDocument.Parse(await _lines.ReadAsync(cancellation).ConfigureAwait(false));
        if (Text(capabilities.RootElement, "type") != "capabilities" || Text(ready.RootElement, "type") != "ready" ||
            !capabilities.RootElement.TryGetProperty("protocolVersion", out var version) || version.GetInt32() != 3)
            throw new MetadataReadException("native_protocol_unsupported");
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is not { } process) return;
        Kill(process);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw new MetadataReadException("native_cleanup_unconfirmed"); }
        process.Dispose();
        _process = null;
        _lines = null;
    }
    private static void Kill(Process process)
    { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }
    private static string Text(JsonElement value, string key) =>
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString()! : "";
    private static int? Integer(JsonElement value, string key) =>
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number) ? number : null;
    private static double Double(JsonElement value, string key) =>
        value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var number) ? number : 0;
}
internal sealed record NativeRequest(string Type, string RequestId, string Path, bool ReadCover, bool AllowUntaggedMp3);
[JsonSerializable(typeof(NativeRequest))]
internal partial class NativeRequestContext : JsonSerializerContext;
