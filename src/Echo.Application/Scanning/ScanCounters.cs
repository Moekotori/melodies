using System.Diagnostics;
using Echo.Contracts.Scanning;

namespace Echo.Application.Scanning;

internal sealed class ScanCounters(string job, Stopwatch watch, IProgress<ScanProgress>? progress)
{
    public long Discovered;
    public long Inserted, Updated, Unchanged, Failed;
    private long _lastPublished;
    public void Publish(string phase, bool force = false)
    {
        if (progress is null || (!force && watch.ElapsedMilliseconds - _lastPublished < 100)) return;
        _lastPublished = watch.ElapsedMilliseconds;
        // A broken presentation subscriber must not abort a committed import.
        try { progress.Report(new(job, phase, Interlocked.Read(ref Discovered),
            Inserted + Updated + Unchanged + Failed, Inserted, Updated, Unchanged, Failed)); }
        catch (Exception) { }
    }
}
