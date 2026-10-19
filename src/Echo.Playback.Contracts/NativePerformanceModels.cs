namespace Echo.Playback;

public sealed record NativeProcessResources(long ProcessId, long WorkingSetBytes, long? PrivateBytes);
public sealed record SdmRealtimePerformance(string State, string Stream, bool IndependentlyCalibrated,
    bool ExecutionMatchesObservation, long? Blocks, long? DeadlineOverruns, double? ProcessingP99Milliseconds,
    long? ReadyDsdBits, long? CapacityDsdBits, long? RenderUnderruns, long? MissingDsdBits);
