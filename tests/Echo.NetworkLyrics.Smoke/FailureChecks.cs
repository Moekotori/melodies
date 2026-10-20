using Echo.Contracts.Lyrics;
using Echo.Core.Lyrics;
using Echo.Lyrics;

static class FailureChecks
{
    public static async Task RunAsync(CancellationToken token)
    {
        var query = Fixtures.Identity;
        using var unavailable = new HttpClient(new FixtureHttp((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable))));
        await using (var matcher = new NetworkLyricsMatcher(unavailable))
        {
            try { await matcher.MatchAsync(query, token); throw new Exception("Outage was reported as no lyrics"); }
            catch (LyricsLookupUnavailableException) { }
        }
        var secondDetail = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var slowSibling = new HttpClient(new FixtureHttp(async (request, cancellation) =>
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath.EndsWith("search", StringComparison.Ordinal))
                return Fixtures.Json("""
                    {"status":200,"data":{"items":[{"id":1,"musicNames":["Song"],"artistNames":["Singer"]},{"id":2,"musicNames":["Song"],"artistNames":["Singer"]}]}}
                    """);
            if (uri.Query.Contains("id=2", StringComparison.Ordinal))
            {
                secondDetail.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellation);
            }
            return Fixtures.Response(request);
        }));
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            var provider = new AmllLyrics(new(slowSibling));
            var result = provider.ReadAsync(query, Task.FromResult<IReadOnlyList<LyricsSong>>([new("42", query)]), cancel.Token);
            await secondDetail.Task.WaitAsync(token); cancel.Cancel();
            if ((await result).Count != 1) throw new Exception("Slow sibling discarded completed TTML");
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        using var slow = new HttpClient(new FixtureHttp(async (_, cancellation) =>
        {
            Interlocked.Increment(ref active); started.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellation); throw new Exception("Unreachable"); }
            finally { Interlocked.Decrement(ref active); }
        }));
        await using (var source = new CoreLyricsSource(new StubLyrics(), (_, _) => Task.FromResult<LyricsIdentity?>(query), true, new(slow)))
        {
            var request = source.ReadAsync("song.flac", null, token);
            await started.Task.WaitAsync(token); source.SetEnabled(false);
            if (await request is not null || active != 0) throw new Exception("Turning off lyrics left HTTP or late results alive");
        }
        // A declared oversize is rejected before reading its body; another provider still works.
        using var oversized = new HttpClient(new FixtureHttp((request, _) =>
        {
            var response = Fixtures.Response(request);
            if (request.RequestUri!.Host == "lrclib.net") response.Content.Headers.ContentLength = 3 * 1024 * 1024;
            return Task.FromResult(response);
        }));
        await using (var matcher = new NetworkLyricsMatcher(oversized))
            if (await matcher.MatchAsync(query, token) is null) throw new Exception("Oversized sibling broke fallback");
        Console.WriteLine("PASS outage errors, partial TTML, disable cancellation and response bounds");
    }
}
