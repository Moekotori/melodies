namespace Echo.Contracts.Scanning;

public sealed record ScanRequest(string Root, int Workers = 2, int BatchSize = 128,
    bool Force = false, bool DetectMissing = false);
public enum ScanStatus { Running, Completed, Cancelled, Failed, Interrupted }
public sealed record ScanIssue(long Ordinal, string Path, string Code);
public sealed record ScanProgress(string JobId, string Phase, long Discovered, long Processed,
    long Inserted, long Updated, long Unchanged, long Failed);
public sealed record ScanReport(string JobId, ScanStatus Status, long Discovered,
    long Inserted, long Updated, long Unchanged, long Failed, long MarkedMissing,
    double ElapsedMilliseconds, string? ErrorCode);
public sealed class MetadataReadException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
