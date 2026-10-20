using System.Net;
using System.Text;
using Echo.Contracts.Radio;
using Echo.Infrastructure.Radio;

internal static class RadioDirectorySmoke
{
    private static readonly RadioDirectoryQuery Popular = new("", "", "", "", 0);

    public static async Task RunAsync(CancellationToken token)
    {
        var waiting = new BlockingDirectory();
        using var directory = new RadioDirectoryClient(waiting, _ => Task.FromResult<IReadOnlyList<string>>(["de1.api.radio-browser.info"]));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
        var first = directory.SearchAsync(Popular, cancel.Token);
        var others = Enumerable.Range(0, 3).Select(_ => directory.SearchAsync(Popular, token)).ToArray();
        Check(waiting.Entered == 4, "Four Control queries must be admitted independently");
        Check((await directory.SearchAsync(Popular, token)).Error == "busy", "Directory concurrency is bounded");
        cancel.Cancel();
        Check((await first).Error == "cancelled" && others.All(task => !task.IsCompleted), "Cancelling one Control must not cancel another");
        waiting.Release.TrySetResult();
        Check((await Task.WhenAll(others)).All(result => result.Error is null), "Other Controls must complete");

        using var malformed = new RadioDirectoryClient(new MalformedDirectory(), _ => Task.FromResult<IReadOnlyList<string>>(["de1.api.radio-browser.info", "de2.api.radio-browser.info"]));
        Check((await malformed.SearchAsync(Popular, token)).Error is null, "Malformed mirror must allow the second attempt");
        var invalidEntry = Encoding.UTF8.GetBytes("[{\"lastcheckok\":999999999999999999999}]");
        Check(RadioDirectoryParser.Parse(invalidEntry, 0).Stations.Count == 0, "Invalid numeric fields must not break a page");
        Console.WriteLine("PASS directory concurrency limit, per-Control cancellation, malformed mirror and numeric input");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static HttpResponseMessage Empty() => new(HttpStatusCode.OK) { Content = new StringContent("[]", Encoding.UTF8, "application/json") };

    private sealed class BlockingDirectory : HttpMessageHandler
    {
        public int Entered;
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Entered);
            await Release.Task.WaitAsync(cancellationToken);
            return Empty();
        }
    }

    private sealed class MalformedDirectory : HttpMessageHandler
    {
        private int calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(++calls == 1
                ? new(HttpStatusCode.OK) { Content = new StringContent("{invalid", Encoding.UTF8, "application/json") }
                : Empty());
    }
}
