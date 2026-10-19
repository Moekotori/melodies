namespace Echo.Application.Playback;

public enum SleepTimerAction { Pause, Stop, Quit }
public sealed record SleepTimerState(bool Active, double RemainingSeconds, SleepTimerAction Action,
    bool Executing = false, string? Error = null);
