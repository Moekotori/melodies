using System.Diagnostics;
using Echo.Application.Scheduling;
using Echo.Contracts.Tasks;
using Echo.Playback;

internal static class PressurePolicySmoke
{
    public static void Run(PlaybackStateSnapshot template)
    {
        var clock = new PolicyClock();
        var policy = new PlaybackWorkPolicy(clock);
        PlaybackStateSnapshot Frame(double? buffer, bool ended = false, long underruns = 0) => template with
        {
            Playback = template.Playback with { Status = PlaybackStatus.Playing, OperationId = 99 },
            Performance = new(99, clock.GetTimestamp(), 3, buffer is { } ms ? (long)(ms * 48) : null, 48000,
                ended, underruns, 0, null, null)
        };
        BackgroundAdmissionSnapshot Observe(PlaybackStateSnapshot state) => policy.Observe(state, clock.GetTimestamp());
        Check(Observe(Frame(1000)).MetadataConcurrency == 1, "new playback had no warmup protection");
        clock.Advance(2100);
        Check(Observe(Frame(1000)).MetadataConcurrency == 2, "healthy playback did not recover bounded parallelism");
        var zero = Frame(0, ended: true);
        Check(Observe(zero).Pressure == PlaybackWorkPressure.Normal, "normal EOF drain was mistaken for starvation");
        Check(Observe(Frame(1000, underruns: 1)).Pressure == PlaybackWorkPressure.Protected,
            "confirmed new underrun did not reduce background pressure");
        clock.Advance(500);
        Check(Observe(Frame(1000, underruns: 1)).Pressure == PlaybackWorkPressure.Protected,
            "protection was removed by a single good sample");
        clock.Advance(600); Observe(Frame(1000, underruns: 1));
        clock.Advance(2100);
        Check(Observe(Frame(1000, underruns: 1)).Pressure == PlaybackWorkPressure.Normal, "stable recovery did not exit protection");
        var stale = Frame(0);
        clock.Advance(3000);
        Check(Observe(stale).Pressure == PlaybackWorkPressure.Reduced, "stale data was treated as fresh buffer starvation");
        Check(Observe(Frame(null)).MetadataConcurrency == 1, "unknown metrics caused unlimited work or permanent suspension");
        var newOperation = Frame(1000) with { Playback = template.Playback with { Status = PlaybackStatus.Playing, OperationId = 100 } };
        Check(Observe(newOperation).Reason == PlaybackWorkReason.TelemetryUnknown, "previous operation metrics authorized a new playback operation");
        Console.WriteLine("PASS startup grace, EOF drain, underrun protection, hysteresis, healthy recovery and stale/missing/wrong-operation policy");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class PolicyClock : TimeProvider
    {
        private long timestamp = Stopwatch.Frequency;
        public override long GetTimestamp() => timestamp;
        public void Advance(int milliseconds) => timestamp += Stopwatch.Frequency * milliseconds / 1000;
    }
}
