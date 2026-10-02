using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Media;

namespace Echo.Playback;

public enum PlaybackStatus { Stopped, Loading, Playing, Paused, Ended, Faulted }
public enum RepeatMode { Off, One, All }
public enum QueuePlaybackMode { Sequential, Shuffle, RepeatOne, RepeatAll }

public sealed record AudioSource(string Kind, string Uri, SourceRange? Range = null,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public static AudioSource Local(string path, double start = 0, double? end = null)
        => new("local", Path.GetFullPath(path), start > 0 || end.HasValue ? new(start, end) : null);

    public void Validate()
    {
        if (Kind == "local")
        {
            if (!Path.IsPathFullyQualified(Uri) || !File.Exists(Uri)) throw new FileNotFoundException("音源不存在", Uri);
            AudioFormatCatalog.RequireDirectAudio(Uri);
            if (Headers is { Count: > 0 }) throw new ArgumentException("本地音源不接受 HTTP headers");
        }
        else if (Kind != "http" || !System.Uri.TryCreate(Uri, UriKind.Absolute, out var url)
                 || url.Scheme is not ("http" or "https") || Range is not null)
            throw new ArgumentException("只支持本地文件或直接 HTTP 音频流");
        if (Range is { } r && (!double.IsFinite(r.StartSeconds) || r.StartSeconds < 0
            || r.EndSeconds is { } e && (!double.IsFinite(e) || e <= r.StartSeconds)))
            throw new ArgumentException("无效的 CUE/片段范围");
        string[] allowed = ["authorization", "cookie", "referer", "origin", "user-agent", "accept"];
        if (Headers is { } headers && (headers.Count > 12 || headers.Sum(x => x.Key.Length + x.Value.Length) > 32768
            || headers.Any(x => !allowed.Contains(x.Key.ToLowerInvariant()) || x.Value.Length > 16384 || x.Value.ContainsAny('\r', '\n'))))
            throw new ArgumentException("无效的 HTTP headers");
    }
}

public sealed record SourceRange(double StartSeconds, double? EndSeconds);
public sealed record QueueTrack(string ItemId, string TrackId, string FilePath, AudioSource? Source = null,
    IReadOnlyDictionary<string, string?>? Metadata = null, bool QueuedNext = false, bool RemoveAfterPlay = false,
    QueueOrigin? Origin = null, int? SampleRate = null);
public sealed record QueueOrigin(string Kind, string Label, string? Id = null);
public sealed record QueueSnapshot(long Version, IReadOnlyList<QueueTrack> Items, IReadOnlyList<QueueTrack> History,
    string? CurrentItemId, QueueTrack? Upcoming, QueuePlaybackMode Mode);
public sealed record NativeQueue(long Revision, string? CurrentItemId, string RepeatMode, IReadOnlyList<QueueTrack> Items);
public sealed record PlaybackSnapshot(long Version, PlaybackStatus Status, QueueTrack? Current,
    double PositionSeconds, double DurationSeconds, double Volume, long OperationId,
    int SampleRate, int Channels, string? Error, JsonElement? Output, JsonElement? Processing)
{
    public double PlaybackRate { get; init; } = 1;
}
public sealed record PlaybackCheckpoint(int SchemaVersion, IReadOnlyList<QueueTrack> Items, string? CurrentItemId,
    string RepeatMode, double PositionSeconds, double Volume, JsonElement? Device,
    IReadOnlyDictionary<string, JsonElement> Dsp, bool Shuffle = false,
    IReadOnlyList<QueueTrack>? History = null, IReadOnlyList<string>? ShuffleOrder = null, QueueTrack? DetachedCurrent = null);

public interface INativeAudio : IAsyncDisposable
{
    event Action<string, JsonElement>? Notification;
    event Action<Exception>? Failed;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NativeQueue))]
[JsonSerializable(typeof(PlaybackCheckpoint))]
internal partial class PlaybackJsonContext : JsonSerializerContext;

internal static class PlaybackJson
{
    public static long Long(this JsonElement e, string name, long fallback = 0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : fallback;
    public static double Number(this JsonElement e, string name, double fallback = 0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : fallback;
    public static string? Text(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    public static bool Flag(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    public static JsonElement? Child(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v.Clone() : null;
    public static bool ContainsAny(this string s, char a, char b) => s.IndexOfAny([a, b]) >= 0;
}
