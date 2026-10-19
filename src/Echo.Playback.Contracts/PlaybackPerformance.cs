using System.Diagnostics;
using System.Text.Json;

namespace Echo.Playback;

public sealed record ProcessingStagePerformance(string Stage, long Timestamp, double? Speed,
    double? LastMilliseconds, double? PeakMilliseconds);
public sealed record PlaybackPerformance(long OperationId, long Timestamp, long HostEpoch,
    long? BufferedFrames, int? PcmSampleRate, bool? InputEnded,
    long? HostUnderrunCallbacks, long? HostUnderrunFrames,
    ProcessingStagePerformance? Src, ProcessingStagePerformance? Sdm)
{
    public NativeProcessResources? NativeResources { get; init; }
    public SdmRealtimePerformance? SdmRealtime { get; init; }
    public double? BufferedMilliseconds => BufferedFrames is { } frames && PcmSampleRate is > 0
        ? frames * 1000d / PcmSampleRate.Value : null;
    public static PlaybackPerformance Unknown { get; } = new(0, 0, 0, null, null, null, null, null, null, null);
}
