namespace Echo.Playback;

[Flags]
public enum PlaybackStateChanges { None = 0, Playback = 1, Queue = 2, Clock = 4, Output = 8, All = 15 }

/// <summary>One owner-published frame; queue, requested output and playback always belong to this same revision.</summary>
public sealed record PlaybackStateSnapshot(Guid Generation, long Version, PlaybackSnapshot Playback,
    QueueSnapshot Queue, PlaybackOutputState Output, long ClockTimestamp, RepeatMode Repeat, bool IsAvailable);
public sealed record PlaybackStateUpdate(PlaybackStateSnapshot State, PlaybackStateChanges Changes, bool IsInitial = false);
public sealed record ZoneSnapshot(string Id, string Name, PlaybackStateSnapshot State);
