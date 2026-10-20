using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Threading;
using Echo.Contracts.Library;
using Echo.Contracts.Lyrics;
using Echo.Core.Client;
using Echo.Desktop.Controls.Lyrics;
using Echo.Desktop.Features.Albums;
using SkiaSharp;

// Isolated production hot paths; no user database, real network, or audio device.
internal static class PerformanceGapsSmoke
{
    public static void Run(bool baseline)
    {
        LayoutHits(baseline);
        JsonComparison(baseline);
        SharedCovers(baseline);
        if (!baseline) { CoverCancellation(); RequestBounds(); }
        Console.WriteLine(baseline ? "BASELINE performance gaps recorded" : "PASS performance gaps: cache hits, JSON state, shared covers and cancellation");
    }

    private static void LayoutHits(bool baseline)
    {
        using var cache = new LyricsTextLayoutCache();
        var line = new LyricLine("Performance fixture", 0, 1000, []);
        var font = Typeface.Default;
        var expected = Get();
        Measure("lyrics cache hit", () => Check(ReferenceEquals(expected, Get()), "layout cache reuses shaping"), baseline);
        LyricsTextLayoutCache.Entry Get() => cache.Get(line, font, 32, 18, 640, TextAlignment.Left,
            Brushes.White, Brushes.Blue, false, false);
    }

    private static void JsonComparison(bool baseline)
    {
        var compare = typeof(CorePlaybackClient).GetMethod("JsonSame", BindingFlags.NonPublic | BindingFlags.Static)!
            .CreateDelegate<Func<JsonElement?, JsonElement?, bool>>();
        var bands = string.Join(',', Enumerable.Range(0, 20).Select(i => $"{{\"hz\":{i * 100 + 20},\"gain\":0,\"q\":1.41}}"));
        using var a = JsonDocument.Parse("{\"eq\":{\"bands\":[" + bands + "]},\"enabled\":true}");
        using var b = JsonDocument.Parse(a.RootElement.GetRawText());
        Measure("playback JSON comparison", () => Check(compare(a.RootElement, b.RootElement), "unchanged output is equal"), baseline);
        Check(compare(null, null) && !compare(a.RootElement, null), "nullable output comparisons");
        using var changed = JsonDocument.Parse("{\"enabled\":false}");
        Check(!compare(a.RootElement, changed.RootElement), "changed output is detected");
        if (!baseline)
        {
            using var reordered = JsonDocument.Parse("{\"enabled\":true,\"eq\":{\"bands\":[" + bands + "]}}");
            Check(!compare(a.RootElement, reordered.RootElement), "raw equality preserves the previous property-order semantics");
        }
    }

    private static void SharedCovers(bool baseline)
    {
        var source = new ArtworkFixture();
        var cache = new AlbumArtworkCache(source);
        var allocated = GC.GetTotalAllocatedBytes(false);
        var elapsed = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, 16).Select(_ => cache.AcquireAsync("same-cover", CancellationToken.None)).ToArray();
        try
        {
            Wait(() => source.Reads > 0);
            source.Ready.TrySetResult(Png());
            Pump(Task.WhenAll(tasks));
            var first = tasks[0].Result!;
            Check(first.Image is not null && tasks.All(t => ReferenceEquals(t.Result?.Image, first.Image)), "all callers share immutable pixels");
            Console.WriteLine($"MEASURE shared cover: callers=16 reads/decodes={source.Reads}, elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F2}, process_allocated_bytes={GC.GetTotalAllocatedBytes(false) - allocated}, resident_bytes={cache.Bytes}");
            if (!baseline) Check(source.Reads == 1, "concurrent cover requests decode once");
        }
        finally
        {
            foreach (var task in tasks) if (task.IsCompletedSuccessfully) task.Result?.Dispose();
            cache.ClearUnused();
            Check(cache.Count == 0 && cache.Bytes == 0, "cover leases release cached pixels");
            Pump(cache.DisposeAsync().AsTask());
        }
    }

    private static void CoverCancellation()
    {
        var source = new ArtworkFixture();
        var cache = new AlbumArtworkCache(source);
        using var cancellation = new CancellationTokenSource();
        var cancelled = cache.AcquireAsync("shared", cancellation.Token);
        var survivor = cache.AcquireAsync("shared", CancellationToken.None);
        Wait(() => source.Reads == 1);
        cancellation.Cancel();
        Pump(cancelled);
        Check(cancelled.Result is null, "one cancelled waiter completes without waiting for I/O");
        source.Ready.TrySetResult(Png()); Pump(survivor);
        Check(survivor.Result?.Image is not null && source.Reads == 1, "one cancellation preserves the other caller");
        survivor.Result!.Dispose(); cache.ClearUnused();
        Pump(cache.DisposeAsync().AsTask());

        source = new(); cache = new(source);
        using var abandoned = new CancellationTokenSource();
        var only = cache.AcquireAsync("abandoned", abandoned.Token);
        Wait(() => source.Reads == 1); abandoned.Cancel(); Pump(only);
        Wait(() => Volatile.Read(ref source.Cancelled) == 1);
        var replacement = cache.AcquireAsync("abandoned", CancellationToken.None);
        Wait(() => source.Reads == 2);
        source.Ready.TrySetResult(Png()); Pump(replacement);
        Check(replacement.Result?.Image is not null, "a cancelled shared request does not poison a later request");
        replacement.Result!.Dispose(); cache.ClearUnused();
        var closing = cache.AcquireAsync("closing", CancellationToken.None);
        Pump(cache.DisposeAsync().AsTask()); Pump(closing);
        Check(closing.Result is null && cache.Bytes == 0, "disposing drops pending work and images");
    }

    private static void RequestBounds()
    {
        var source = new ArtworkFixture();
        var cache = new AlbumArtworkCache(source);
        var requests = Enumerable.Range(0, 64).Select(_ => cache.AcquireAsync("bounded", CancellationToken.None)).ToArray();
        var overflow = cache.AcquireAsync("another", CancellationToken.None);
        Check(overflow.IsCompletedSuccessfully && overflow.Result is null, "waiting callers have a global bound");
        source.Ready.TrySetResult(Png()); Pump(Task.WhenAll(requests));
        Check(source.Reads == 1 && requests.All(r => r.Result?.Image is not null), "bounded callers share one decoder");
        foreach (var request in requests) request.Result!.Dispose();
        cache.ClearUnused(); Pump(cache.DisposeAsync().AsTask());
        Check(cache.Bytes == 0 && cache.Count == 0, "bound test releases resources");
    }

    private static void Measure(string name, Action action, bool baseline)
    {
        for (var i = 0; i < 1024; i++) action();
        const int samples = 5000;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < samples; i++) action();
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Console.WriteLine($"MEASURE {name}: samples={samples}, allocated_bytes={bytes}, elapsed_ms={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F2}");
        if (!baseline) Check(bytes < 64 * samples, name + " avoids repeated key/JSON-string allocation");
    }

    private static byte[] Png()
    {
        using var pixels = new SKBitmap(640, 640); pixels.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(pixels);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }
    private static void Pump(Task task) { Wait(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Wait(Func<bool> ready)
    {
        var start = Stopwatch.GetTimestamp();
        while (!ready())
        {
            if (Stopwatch.GetElapsedTime(start).TotalSeconds > 5) throw new TimeoutException("Performance fixture timed out.");
            Dispatcher.UIThread.RunJobs(); Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class ArtworkFixture : IAlbumArtwork
    {
        public readonly TaskCompletionSource<byte[]?> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads, Cancelled;
        public async Task<byte[]?> ReadAsync(string path, CancellationToken cancellation)
        {
            Interlocked.Increment(ref Reads);
            try { return await Ready.Task.WaitAsync(cancellation).ConfigureAwait(false); }
            catch (OperationCanceledException) { Interlocked.Increment(ref Cancelled); throw; }
        }
    }
}
