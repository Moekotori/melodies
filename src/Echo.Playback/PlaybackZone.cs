using System.Text.Json;

namespace Echo.Playback;

/// <summary>A stable playback area. Its session owns the queue; changing output does not change its identity.</summary>
public sealed class PlaybackZone
{
    public const string LocalId = "local";
    public string Id { get; }
    public string Name { get; }
    public PlaybackSessionCoordinator Session { get; }

    public PlaybackZone(string id, string name, PlaybackSessionCoordinator session)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(session);
        Id = id; Name = name; Session = session;
    }

    public PlaybackOutputState Output => PlaybackOutputState.From(Session.Snapshot, Session.RequestedOutput);
}

/// <summary>Device facts are separate from requested settings and the zone's queue/session.</summary>
public sealed record PlaybackOutputState(string DeviceName, string? Backend, bool HasConfirmedDevice,
    bool IsRendering, JsonElement? Requested, JsonElement? Confirmed)
{
    public static PlaybackOutputState From(PlaybackSnapshot playback, JsonElement? requested)
    {
        var confirmed = playback.OperationId > 0
            && playback.Status is PlaybackStatus.Loading or PlaybackStatus.Playing or PlaybackStatus.Paused
            ? playback.Output : null;
        var name = confirmed?.Text("deviceName") ?? requested?.Text("deviceName");
        return new(string.IsNullOrWhiteSpace(name) ? "系统默认输出" : name, confirmed?.Text("backend"),
            confirmed.HasValue, confirmed.HasValue && playback.Status == PlaybackStatus.Playing, requested, confirmed);
    }
}
