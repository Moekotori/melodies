using System.Diagnostics;

internal sealed class StressReadMetrics : IAsyncDisposable
{
    private readonly string root;
    private readonly string label;
    private readonly StreamWriter results;
    private readonly CancellationTokenSource stop = new();
    private readonly Task sampling;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly long startAllocated = GC.GetTotalAllocatedBytes(true);
    private long peakPrivate;
    public int Failures { get; private set; }
    public int OverBudget { get; private set; }

    public StressReadMetrics(string root, string label)
    {
        this.root = root; this.label = label;
        results = new(Path.Combine(root, label + "-queries.csv"));
        results.WriteLine("scenario,firstMs,medianMs,p95Ms,maxMs,allocatedBytesPerWarmCall,targetP95Ms,status");
        results.Flush(); sampling = SampleAsync();
    }

    public async Task MeasureAsync(string scenario, Func<CancellationToken, Task> read, CancellationToken token, int samples = 20, double targetMs = 100)
    {
        var watch = Stopwatch.StartNew(); double first = 0;
        try
        {
            using (var budget = CancellationTokenSource.CreateLinkedTokenSource(token))
            { budget.CancelAfter(TimeSpan.FromSeconds(5)); await read(budget.Token); }
            first = watch.Elapsed.TotalMilliseconds;
            var times = new double[samples];
            var allocated = GC.GetTotalAllocatedBytes(true);
            for (var i = 0; i < samples; i++)
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
                budget.CancelAfter(TimeSpan.FromSeconds(5));
                watch.Restart(); await read(budget.Token); times[i] = watch.Elapsed.TotalMilliseconds;
            }
            allocated = GC.GetTotalAllocatedBytes(true) - allocated;
            Array.Sort(times);
            var p95 = times[(int)Math.Ceiling(samples * .95) - 1];
            var status = p95 > targetMs ? "over-budget" : "ok";
            if (p95 > targetMs) OverBudget++;
            Write(FormattableString.Invariant($"{scenario},{first:F3},{times[samples / 2]:F3},{p95:F3},{times[^1]:F3},{allocated / samples},{targetMs:F1},{status}"));
        }
        catch (Exception error) when (!token.IsCancellationRequested)
        {
            Failures++;
            Write(FormattableString.Invariant($"{scenario},{first:F3},0,0,{watch.Elapsed.TotalMilliseconds:F3},0,{targetMs:F1},{error.GetType().Name}"));
            Console.WriteLine(scenario + ": " + error.Message);
        }
    }

    private void Write(string line) { results.WriteLine(line); results.Flush(); Console.WriteLine(label + " " + line); }

    private async Task SampleAsync()
    {
        using var process = Process.GetCurrentProcess();
        using var log = new StreamWriter(Path.Combine(root, label + "-process.csv"));
        log.WriteLine("elapsedMs,privateBytes,workingSetBytes,cpuMs,managedHeapBytes");
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            do
            {
                process.Refresh(); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
                log.WriteLine(FormattableString.Invariant($"{elapsed.Elapsed.TotalMilliseconds:F1},{process.PrivateMemorySize64},{process.WorkingSet64},{process.TotalProcessorTime.TotalMilliseconds:F1},{GC.GetTotalMemory(false)}"));
                log.Flush();
            } while (await timer.WaitForNextTickAsync(stop.Token));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); await sampling; results.Dispose(); stop.Dispose();
        var line = FormattableString.Invariant($"elapsedMs={elapsed.Elapsed.TotalMilliseconds:F1} sampledPeakPrivateBytes={peakPrivate} allocatedBytes={GC.GetTotalAllocatedBytes(true) - startAllocated} failedScenarios={Failures} overBudgetScenarios={OverBudget}");
        await File.WriteAllTextAsync(Path.Combine(root, label + "-summary.txt"), line);
        Console.WriteLine(label + " " + line);
    }
}
