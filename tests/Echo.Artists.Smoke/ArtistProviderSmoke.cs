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
        Check(result.Biography?.ArtworkReference is { Length: 64 } && handler.Images == 1,
            "A matched biography image is downloaded once into the Core-owned bounded image store.");
        Check(result.Links.Count(link => link.Label == "social network") == 2,
            "Social accounts after fourteen ordinary external links survive provider ingestion.");
        Check(await service.ReadImageAsync(result.Biography!.ArtworkReference!, default) is { Length: > 0 }
            && await service.ReadImageAsync("../../secret", default) is null, "Opaque image references cannot read arbitrary paths.");
        var calls = handler.Calls;
        var cached = await service.GetArtistInformationAsync(new("Aurora"));
        Check(handler.Calls == calls && cached.Biography is not null, "A healthy disk cache avoids network calls.");
        var detailed = await service.GetArtistInformationAsync(new("Aurora", IncludeFacts: true));
        Check(detailed.PublicFacts is { Match: "wikipedia", Name: "Aurora" } details && details.Aliases.Contains("欧若拉")
            && details.Facts.Any(fact => fact is { Label: "活动开始", Value: "2012" })
            && details.Members.Any(member => member.Name == "Partner" && member.Role.Contains("2019")),
            "Requested facts retain matched identity, aliases, qualified membership dates and public facts.");
        Check(CoreJson.Read<ArtistInformation>(CoreJson.Value(detailed)).PublicFacts?.Aliases.Count == 1, "Public facts use AOT RPC serialization.");
        handler.UnsafeImage = true;
        var unsafeImage = await service.GetArtistInformationAsync(new("Aurora", Refresh: true));
        Check(unsafeImage.Biography is { ArtworkReference: null } && handler.Images == 1, "An external thumbnail cannot cause arbitrary network image requests.");
        handler.UnsafeImage = false; handler.LargeImage = true;
        var largeImage = await service.GetArtistInformationAsync(new("Aurora", Refresh: true));
        Check(largeImage.Biography is { ArtworkReference: null }, "Oversized image responses retain biography without allocating an unbounded image.");
        handler.LargeImage = false;
        var restoredImage = await service.GetArtistInformationAsync(new("Aurora", Refresh: true));
        Check(restoredImage.Biography?.ArtworkReference is { Length: 64 } && restoredImage.Status == "ready",
            "A healthy refresh restores the image reference after a rejected thumbnail.");
        handler.FailEvents = true;
        var partial = await service.GetArtistInformationAsync(new("Aurora", Refresh: true));
        Check(partial is { Status: "partial", Biography: not null, EventStatus: "stale" } && partial.Facts.Count > 0 && partial.Events.Count == 1,
            "Event failure retains successful siblings and labels the cached event as stale.");
        handler.FailAll = true;
        var stale = await service.GetArtistInformationAsync(new("Aurora", Refresh: true));
        Check(stale is { Status: "stale", Biography: not null } && stale.Errors.Count > 0, "Offline refresh retains a labeled healthy cache.");
        handler.FailAll = false; handler.Ambiguous = true;
        var ambiguous = await service.GetArtistInformationAsync(new("Duplicate", Refresh: true));
        Check(ambiguous.Facts.Count == 0 && ambiguous.Relations.Count == 0 && ambiguous.Events.Count == 0, "Ambiguous identities cannot supply facts or events.");
        handler.Block = true;
        using var last = new CancellationTokenSource();
        var stopped = service.GetArtistInformationAsync(new("Stop"), last.Token);
        await handler.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(3)); last.Cancel();
        try { await stopped; throw new Exception("Last caller cancellation was ignored."); } catch (OperationCanceledException) { }
        await handler.Aborted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var json = CoreJson.Value(result);
        Check(CoreJson.Read<ArtistInformation>(json).Biography?.Text == result.Biography?.Text, "Artist information supports AOT source-generated RPC serialization.");
        Console.WriteLine("PASS artist providers: shared cancellation, bounded sources, disk cache, partial failure, offline fallback, ambiguous identity, AOT RPC serialization.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class ProviderHandler : HttpMessageHandler
    {
        public int Calls, Searches, Images;
        public bool FailEvents, FailAll, Ambiguous, Block, UnsafeImage, LargeImage;
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Aborted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Calls); await Task.Delay(15, token);
            if (Block)
            {
                Blocked.TrySetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                catch (OperationCanceledException) { Aborted.TrySetResult(); throw; }
            }
            var url = request.RequestUri!; string body;
            if (FailAll || FailEvents && url.AbsolutePath.Contains("/event/")) return new(HttpStatusCode.ServiceUnavailable);
            if (url.Host is "www.eventernote.com" or "eplus.jp")
                return new(HttpStatusCode.OK) { Content = new StringContent("<main></main>", Encoding.UTF8, "text/html") };
            if (url.Host == "upload.wikimedia.org")
            {
                Interlocked.Increment(ref Images);
                var image = new ByteArrayContent(ArtistArtworkFixture.Pixels());
                image.Headers.ContentType = new("image/png");
                if (LargeImage) image.Headers.ContentLength = 2 * 1024 * 1024;
                return new(HttpStatusCode.OK) { Content = image };
            }
            if (url.Host == "www.wikidata.org")
                body = url.Query.Contains("props=claims", StringComparison.Ordinal)
                    ? """{"entities":{"Q42":{"labels":{"en":{"value":"Aurora"}},"aliases":{"zh-hans":[{"value":"欧若拉"}]},"claims":{"P136":[{"rank":"normal","mainsnak":{"datavalue":{"value":{"id":"Q1"}}}}],"P2031":[{"mainsnak":{"datavalue":{"value":{"time":"+2012-00-00T00:00:00Z"}}}}],"P527":[{"mainsnak":{"datavalue":{"value":{"id":"Q3"}}},"qualifiers":{"P580":[{"datavalue":{"value":{"time":"+2019-00-00T00:00:00Z"}}}]}}]}}}}"""
                    : """{"entities":{"Q1":{"labels":{"zh-hans":{"value":"艺术流行"}}},"Q3":{"labels":{"en":{"value":"Partner"}}}}}""";
            else if (url.Host.EndsWith("wikipedia.org", StringComparison.Ordinal))
            {
                body = url.Query.Contains("list=search", StringComparison.Ordinal)
                    ? """{"query":{"search":[{"title":"Aurora"}]}}"""
                    : """{"query":{"pages":[{"title":"Aurora","extract":"Aurora is a Norwegian singer and songwriter.","fullurl":"https://en.wikipedia.org/wiki/Aurora_(singer)","thumbnail":{"source":"IMAGE"},"pageprops":{"wikibase_item":"Q42"}}]}}"""
                        .Replace("IMAGE", UnsafeImage ? "https://untrusted.example/portrait.png" : "https://upload.wikimedia.org/wikipedia/commons/portrait.png" + (LargeImage ? "?large=1" : ""));
            }
            else if (url.AbsolutePath.EndsWith("/artist/", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref Searches);
                body = Ambiguous ? """{"artists":[{"id":"00000000-0000-0000-0000-000000000001","name":"Duplicate"},{"id":"00000000-0000-0000-0000-000000000002","name":"Duplicate"}]}"""
                    : """{"artists":[{"id":"00000000-0000-0000-0000-000000000001","name":"Aurora"}]}""";
            }
            else if (url.AbsolutePath.Contains("/event/", StringComparison.Ordinal))
                body = """{"events":[{"id":"00000000-0000-0000-0000-000000000003","name":"Future concert","life-span":{"begin":"2099-10-19"}}]}""";
            else body = """{"name":"Aurora","type":"Person","area":{"name":"Norway"},"life-span":{"begin":"1996-06-15"},"relations":[{"type":"collaboration","artist":{"id":"00000000-0000-0000-0000-000000000004","name":"Björk"}}]}""";
            if (url.Host == "musicbrainz.org" && body.Contains("\"type\":\"Person\""))
                body = body.Replace("\"relations\":[", "\"relations\":[" + string.Join(',', Enumerable.Range(0, 14).Select(index => "{\"type\":\"other databases\",\"url\":{\"resource\":\"https://database.example/" + index + "\"}}"))
                    + "," + """{"type":"social network","url":{"resource":"https://instagram.com/aurora"}},{"type":"social network","url":{"resource":"https://x.com/aurora"}},""");
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
