using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

internal sealed partial class SubsonicFileClient
{
    private readonly Dictionary<string, (bool Legacy, long Expires)> lyricModes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> lyricMisses = new(StringComparer.Ordinal);
    private readonly object lyricGate = new();
    public async Task<string?> ReadServerLyricsAsync(RemoteConnection source, string remotePath, CancellationToken token)
    {
        var id = RemoteBrowseRules.VirtualId(remotePath).Id;
        if (LyricMiss(source.Id, id)) return null;
        var legacy = LyricMode(source.Id);
        var failed = false; var responded = false;
        string? text = null;
        if (legacy != true)
        {
            try
            {
                text = await StructuredAsync(source, id, token).ConfigureAwait(false);
                responded = true; SetMode(source.Id, false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (Unsupported(error)) { SetMode(source.Id, true); }
            catch (Exception) { failed = true; }
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            try
            {
                text = await LegacyAsync(source, id, token).ConfigureAwait(false);
                responded = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception error) when (Unsupported(error)) { }
            catch (Exception) { failed = true; }
        }
        if (responded && !failed && string.IsNullOrWhiteSpace(text)) MarkLyricMiss(source.Id, id);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
    private async Task<string?> StructuredAsync(RemoteConnection source, string id, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var json = await http.JsonAsync(Url(source, "getLyricsBySongId", new() { ["id"] = id }), deadline.Token).ConfigureAwait(false);
        var blocks = Many(Property(LyricsBody(json.RootElement), "lyricsList"), "structuredLyrics").Take(16).ToArray();
        string? best = null; var bestLines = -1; var synced = false;
        foreach (var block in blocks)
        {
            var lines = Lines(block, true);
            if (lines.Length == 0) continue;
            var timed = Property(block, "synced").ValueKind == JsonValueKind.True && lines.Contains('[');
            if (best is null || (timed && !synced) || (timed == synced && lines.Length > bestLines)) { best = lines; bestLines = lines.Length; synced = timed; }
        }
        return best;
    }
    private async Task<string?> LegacyAsync(RemoteConnection source, string id, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        string artist = ""; string title = "";
        using (var songJson = await http.JsonAsync(Url(source, "getSong", new() { ["id"] = id }), deadline.Token).ConfigureAwait(false))
        {
            var song = Property(LyricsBody(songJson.RootElement), "song");
            artist = RemoteBrowseRules.Text(song, "artist", 512); title = RemoteBrowseRules.Text(song, "title", 512);
        }
        using var json = await http.JsonAsync(Url(source, "getLyrics", new() { ["artist"] = artist, ["title"] = title }), deadline.Token).ConfigureAwait(false);
        var value = RemoteBrowseRules.Text(Property(LyricsBody(json.RootElement), "lyrics"), "value", 1_000_000);
        return value.Length == 0 ? null : Lines(value);
    }
    private static JsonElement LyricsBody(JsonElement json)
    {
        var response = Property(json, "subsonic-response");
        if (RemoteBrowseRules.Text(response, "status") == "ok") return response;
        var error = Property(response, "error");
        var code = RemoteBrowseRules.Number(error, "code");
        throw new LyricEndpointException(code is long number && number <= int.MaxValue ? (int)number : null, RemoteBrowseRules.Text(error, "message", 400));
    }
    private static string Lines(JsonElement block, bool timed)
    {
        var offset = RemoteBrowseRules.Number(block, "offset") ?? 0;
        var builder = new System.Text.StringBuilder(); var count = 0;
        foreach (var line in Many(block, "line"))
        {
            if (count++ == 4000 || builder.Length > 1_000_000) break;
            var text = RemoteBrowseRules.Text(line, "value", 4096).Trim();
            if (text.Length == 0) continue;
            var start = RemoteBrowseRules.Number(line, "start");
            if (builder.Length > 0) builder.Append('\n');
            if (timed && start is long ms) builder.Append(Stamp(ms + offset));
            builder.Append(text);
        }
        return builder.Length == 0 ? "" : builder.ToString();
    }
    private static string Lines(string value)
    {
        var builder = new System.Text.StringBuilder(); var count = 0;
        foreach (var line in value.Split('\n'))
        {
            if (count++ == 4000 || builder.Length > 1_000_000) break;
            var text = line.Trim('\r', ' ');
            if (text.Length == 0) continue;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(text.Length > 4096 ? text[..4096] : text);
        }
        return builder.ToString();
    }
    private static string Stamp(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        return $"[{milliseconds / 60000:00}:{(milliseconds % 60000) / 1000d:00.00}]";
    }
    private static bool Unsupported(Exception error) => error switch
    {
        RemoteHttpException http when http.Status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented => true,
        LyricEndpointException endpoint when endpoint.Code == 70 || (endpoint.Code is null or 0 && UnsupportedPattern().IsMatch(endpoint.Message)) => true,
        _ => false,
    };
    private bool? LyricMode(string sourceId)
    {
        lock (lyricGate)
        {
            if (!lyricModes.TryGetValue(sourceId, out var mode) || mode.Expires <= Environment.TickCount64) { lyricModes.Remove(sourceId); return null; }
            return mode.Legacy;
        }
    }
    private void SetMode(string sourceId, bool legacy)
    {
        lock (lyricGate)
        {
            if (lyricModes.Count >= 128 && !lyricModes.ContainsKey(sourceId)) lyricModes.Clear();
            lyricModes[sourceId] = (legacy, Environment.TickCount64 + 30 * 60 * 1000);
        }
    }
    private bool LyricMiss(string sourceId, string songId)
    {
        var key = sourceId + "\n" + songId;
        lock (lyricGate)
        {
            if (!lyricMisses.TryGetValue(key, out var expiry)) return false;
            if (expiry > Environment.TickCount64) return true;
            lyricMisses.Remove(key); return false;
        }
    }
    private void MarkLyricMiss(string sourceId, string songId)
    {
        var key = sourceId + "\n" + songId;
        lock (lyricGate)
        {
            if (lyricMisses.Count >= 2048) lyricMisses.Clear();
            lyricMisses[key] = Environment.TickCount64 + 10 * 60 * 1000;
        }
    }
    [GeneratedRegex("not implemented|unsupported|not supported|unknown (method|endpoint)|(?:method|endpoint).*not found", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsupportedPattern();
    private sealed class LyricEndpointException(int? code, string message) : IOException(message)
    {
        public int? Code { get; } = code;
    }
}
