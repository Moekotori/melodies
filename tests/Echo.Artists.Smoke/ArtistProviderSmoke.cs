using System.Net;
using System.Text;
using System.Text.Json;
using Echo.ArtistMetadata;
using Echo.Contracts.Library;
using Echo.Core.Contracts;

internal static class ArtistProviderSmoke
{
    public static async Task RunAsync(string root)
    {
        var handler = new ProviderHandler();
        await using var service = new ArtistInformationService(root, handler);
        using var first = new CancellationTokenSource();
        var canceled = service.GetArtistInformationAsync(new("Aurora"), first.Token);
        var kept = service.GetArtistInformationAsync(new("Aurora"));
        first.Cancel();
        try { await canceled; throw new Exception("A canceled caller completed."); } catch (OperationCanceledException) { }
        var result = await kept;
        Check(result is { Status: "ready", Biography: not null, EventStatus: "ready" } && result.Events.Count == 1 && result.Relations.Count == 1 && result.Facts.Any(fact => fact is { Label: "音乐风格", Value: "艺术流行" }),
            "Shared provider work retains the other caller, identity, facts, biography, relations and events.");
        Check(handler.Searches == 1, "Concurrent callers share the provider operation.");
        var calls = handler.Calls;
        var cached = await service.GetArtistInformationAsync(new("Aurora"));
        Check(handler.Calls == calls && cached.Biography is not null, "A healthy disk cache avoids network calls.");
        handler.FailEvents = true;
        var partial = await service.GetArtistInformationAsync(new("Aurora", Refresh: true));
        Check(partial is { Status: "partial", Biography: not null, EventStatus: "stale" } && partial.Facts.Count > 0 && partial.Events.Count == 1,
            "Event failure retains successful siblings and labels the cached event as stale.");
        handler.FailAll = true;
        var stale = await service.GetArtistInformationAsync(new("Aurora", Refresh: true));
        Check(stale is { Status: "stale", Biography: not null } && stale.Errors.Count > 0, "Offline refresh retains a labeled healthy cache.");
        handler.FailAll = false; handler.Ambiguous = true;
        var ambiguous = await service.GetArtistInformationAsync(new("Duplicate", Refresh: true));
        Check(ambiguous.Facts.Count == 0 && ambiguous.Relations.Count == 0 && ambiguous.EventStatus == "unavailable", "Ambiguous identities cannot supply facts or events.");
        var json = CoreJson.Value(result);
        Check(CoreJson.Read<ArtistInformation>(json).Biography?.Text == result.Biography?.Text, "Artist information supports AOT source-generated RPC serialization.");
        Console.WriteLine("PASS artist providers: shared cancellation, bounded sources, disk cache, partial failure, offline fallback, ambiguous identity, AOT RPC serialization.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class ProviderHandler : HttpMessageHandler
    {
        public int Calls, Searches;
        public bool FailEvents, FailAll, Ambiguous;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); await Task.Delay(15, token);
            var url = request.RequestUri!; string body;
            if (FailAll || FailEvents && url.AbsolutePath.Contains("/event/")) return new(HttpStatusCode.ServiceUnavailable);
            if (url.Host == "www.wikidata.org")
                body = url.Query.Contains("props=claims", StringComparison.Ordinal)
                    ? """{"entities":{"Q42":{"claims":{"P136":[{"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q1"}}}}]}}}}"""
                    : """{"entities":{"Q1":{"labels":{"zh-hans":{"value":"艺术流行"}}}}}""";
            else if (url.Host.EndsWith("wikipedia.org", StringComparison.Ordinal))
            {
                body = url.Query.Contains("list=search", StringComparison.Ordinal)
                    ? """{"query":{"search":[{"title":"Aurora"}]}}"""
                    : """{"query":{"pages":[{"title":"Aurora","extract":"Aurora is a Norwegian singer and songwriter.","fullurl":"https://en.wikipedia.org/wiki/Aurora_(singer)","pageprops":{"wikibase_item":"Q42"}}]}}""";
            }
            else if (url.AbsolutePath.EndsWith("/artist/", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Searches);
                body = Ambiguous ? """{"artists":[{"id":"00000000-0000-0000-0000-000000000001","name":"Duplicate"},{"id":"00000000-0000-0000-0000-000000000002","name":"Duplicate"}]}"""
                    : """{"artists":[{"id":"00000000-0000-0000-0000-000000000001","name":"Aurora"}]}""";
            }
            else if (url.AbsolutePath.Contains("/event/", StringComparison.Ordinal))
                body = """{"events":[{"id":"00000000-0000-0000-0000-000000000003","name":"Future concert","life-span":{"begin":"2099-10-19"}}]}""";
            else body = """{"name":"Aurora","type":"Person","area":{"name":"Norway"},"life-span":{"begin":"1996-06-15"},"relations":[{"type":"collaboration","artist":{"id":"00000000-0000-0000-0000-000000000004","name":"Partner"}}]}""";
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
