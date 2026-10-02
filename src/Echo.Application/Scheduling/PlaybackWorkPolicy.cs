using Echo.Contracts.Tasks;
using Echo.Playback;

namespace Echo.Application.Scheduling;

/// <summary>Admission policy only; never changes audio, interrupts a transaction or infers missing telemetry as zero.</summary>
public sealed class PlaybackWorkPolicy(TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private Guid generation;
    private long operation, epoch = -1;
    private long? underruns;
    private long? playingSince, lowSince, protectedSince, healthySince;
    private PlaybackWorkPressure pressure;

    public BackgroundAdmissionSnapshot Observe(PlaybackStateSnapshot state, long now)
    {
        var playback = state.Playback;
        var metrics = state.Performance;
        var increased = epoch == metrics.HostEpoch && underruns is { } before
            && metrics.HostUnderrunCallbacks is { } after && after > before;
        epoch = metrics.HostEpoch; underruns = metrics.HostUnderrunCallbacks;
        if (state.IsAvailable && playback.Status == PlaybackStatus.Faulted && playback.OperationId > 0)
        { pressure = PlaybackWorkPressure.Reduced; return Result(PlaybackWorkReason.TelemetryUnknown); }
        if (!state.IsAvailable || playback.Status is not (PlaybackStatus.Playing or PlaybackStatus.Loading))
        {
            playingSince = lowSince = protectedSince = healthySince = null;
            pressure = PlaybackWorkPressure.Normal;
            return Result(PlaybackWorkReason.None);
        }
        if (generation != state.Generation || operation != playback.OperationId || playingSince is null)
        {
            generation = state.Generation; operation = playback.OperationId;
            playingSince = now; lowSince = protectedSince = healthySince = null;
            pressure = PlaybackWorkPressure.Reduced; increased = false;
        }
        if (playback.Status == PlaybackStatus.Loading || playback.OperationId <= 0)
        { pressure = PlaybackWorkPressure.Reduced; return Result(PlaybackWorkReason.PreparingPlayback); }
        var fresh = metrics.OperationId == playback.OperationId && metrics.Timestamp > 0
            && Age(metrics.Timestamp, now) <= 2000;
        var buffer = fresh ? metrics.BufferedMilliseconds : null;
        bool Slow(ProcessingStagePerformance? stage) => stage?.Speed is > 0 and < 1
            && Age(stage.Timestamp, now) <= 2500;
        var slow = Slow(metrics.Src) || Slow(metrics.Sdm);
        var low = buffer is < 30 && metrics.InputEnded == false;
        if (!low) lowSince = null;
        else lowSince ??= now;
        var warmed = Age(playingSince.Value, now) >= 500;
        if (warmed && fresh && metrics.InputEnded == false && (increased || lowSince is { } start && Age(start, now) >= 100))
        {
            pressure = PlaybackWorkPressure.Protected; protectedSince = now; healthySince = null;
            return Result(increased ? PlaybackWorkReason.Underrun : PlaybackWorkReason.LowBuffer);
        }
        if (pressure == PlaybackWorkPressure.Protected && protectedSince is { } held && Age(held, now) < 1000)
            return Result(PlaybackWorkReason.Recovering);
        var healthy = buffer is >= 150 && !slow || fresh && metrics.InputEnded == true;
        if (healthy)
        {
            healthySince ??= now;
            if (Age(healthySince.Value, now) >= 2000)
            { pressure = PlaybackWorkPressure.Normal; protectedSince = null; return Result(PlaybackWorkReason.None); }
        }
        else healthySince = null;
        pressure = PlaybackWorkPressure.Reduced;
        return Result(buffer is null ? PlaybackWorkReason.TelemetryUnknown : slow ? PlaybackWorkReason.SlowStage
            : healthy ? PlaybackWorkReason.Recovering : PlaybackWorkReason.LowBuffer);
    }

    private double Age(long start, long now) => time.GetElapsedTime(start, now).TotalMilliseconds;
    private BackgroundAdmissionSnapshot Result(PlaybackWorkReason reason) => new(pressure, reason,
        pressure switch { PlaybackWorkPressure.Normal => playingSince is null ? 4 : 2, PlaybackWorkPressure.Reduced => 1, _ => 0 },
        pressure == PlaybackWorkPressure.Normal ? 256 : 32, 0, 0);
}
