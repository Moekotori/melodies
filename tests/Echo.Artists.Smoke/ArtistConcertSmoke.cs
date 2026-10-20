using System.Net;
using System.Text;
using Echo.ArtistMetadata;
using Echo.Contracts.Library;
using Echo.Core.Contracts;

internal static class ArtistConcertSmoke
{
    internal static async Task RunAsync(string root)
    {
        var handler = new ConcertHandler(); await using var service = new ArtistInformationService(root, handler);
        var first = await service.GetArtistInformationAsync(new("Aimer"));
        Check(first.Events.Count == 2 && first.EventStatus == "ready" && handler.ActorPages == 1,
            "Exact Eventernote actor pages and MusicBrainz events merge into one upcoming concert list.");
        var japanese = first.Events.Single(item => item.Source == "Eventernote");
        Check(japanese is { Venue: "Tokyo Garden Theater", Time: "19:30", Timezone: "Asia/Tokyo" }
            && first.Events.Single(item => item.Source == "MusicBrainz") is { Venue: "Echo Arena", Time: "18:00", Region: "Norway" },
            "Concerts preserve venue, local starting time, timezone, region and source.");
        Check(CoreJson.Read<ArtistInformation>(CoreJson.Value(first)).Events[0].Source == "Eventernote", "Concert fields survive AOT Core serialization.");
        var calls = handler.Calls; _ = await service.GetArtistInformationAsync(new("Aimer"));
        Check(handler.Calls == calls, "A healthy concert cache avoids repeated page requests.");
        handler.FailPages = true;
        var stale = await service.GetArtistInformationAsync(new("Aimer", Refresh: true));
        Check(stale.EventStatus == "stale" && stale.Events.Count == 2 && stale.EventFailures is { Count: 2 },
            "Page-source failures retain cached concerts and fresh MusicBrainz events with a stale label.");
        handler.FailPages = false; handler.EmptyActor = true;
        var fallback = await service.GetArtistInformationAsync(new("Aimer", Refresh: true));
        Check(fallback.Events.Count == 2 && fallback.Events.Any(item => item is { Source: "eplus", Time: "18:30", TicketUrl: not null })
            && fallback.Events.All(item => !item.Name.Contains("Other Artist")), "eplus fallback retains matching ticket links and rejects unrelated listings.");
        handler.Captcha = true;
        var blocked = await service.GetArtistInformationAsync(new("Aimer", Refresh: true));
        Check(blocked.Events.Count == 2 && blocked.EventFailures!.Contains("Eventernote") && blocked.EventStatus == "partial",
            "A blocked source cannot masquerade as an empty successful concert lookup or erase successful siblings.");
        handler.Captcha = false; handler.EmptyAll = true;
        var empty = await service.GetArtistInformationAsync(new("Aimer", Refresh: true));
        Check(empty.EventStatus == "ready" && empty.Events.Count == 0, "A successful empty response clears expired cached listings without invented events.");
        handler.Ambiguous = true;
        var unmatched = await service.GetArtistInformationAsync(new("Aimer", Refresh: true));
        Check(unmatched.Events.Count == 0 && unmatched.EventStatus == "ready" && unmatched.EventFailures is { Count: 0 },
            "An ambiguous MusicBrainz identity is an unmatched result, not a connection failure.");
        calls = handler.Calls; _ = await service.GetArtistInformationAsync(new("Aimer"));
        Check(handler.Calls == calls, "Verified empty lookups use a short cache instead of repeatedly fetching all concert sources.");
        Console.WriteLine("PASS concerts: Eventernote identity, eplus fallback, MusicBrainz cancellation/type/date filters, venue/time, ticket links, cache, partial failure, empty results and AOT DTOs.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private sealed class ConcertHandler : HttpMessageHandler
    {
        internal int Calls, ActorPages;
        internal bool FailPages, EmptyActor, Captcha, EmptyAll, Ambiguous;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Interlocked.Increment(ref Calls); var uri = request.RequestUri!;
            string body; var html = uri.Host is "www.eventernote.com" or "eplus.jp";
            if (html && FailPages) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            if (uri.Host == "www.eventernote.com")
            {
                if (Captcha) body = "<title>Just a moment</title><div id=cf-chl-blocked>captcha</div>";
                else if (EmptyActor || EmptyAll) body = "<main></main>";
                else if (uri.AbsolutePath == "/actors/search") body = "<a href='/actors/Other/1'>Other Artist</a><a href='/actors/Aimer/2'>Aimer</a>";
                else if (uri.AbsolutePath == "/actors/Aimer/2/events")
                {
                    Interlocked.Increment(ref ActorPages);
                    body = """<ul><li class="clearfix "><p class="day">2099-11-03</p><h4><a href="/events/101">Festival Night</a></h4>会場: <a>Tokyo Garden Theater</a><span>開演 19:30</span></li><li class="clearfix"><p class="day">2000-01-01</p><h4><a href="/events/102">Past event</a></h4></li><li class="clearfix"><p class="day">2099-12-01</p><h4><a href="https://evil.example/events/103">Wrong destination</a></h4></li></ul>""";
                }
                else body = "<main></main>";
            }
            else if (uri.Host == "eplus.jp") body = EmptyAll ? "<main><h1>Aimer search results</h1><a href='/sf/detail/444'>Other Artist 2099/11/5(木)18:30</a></main>"
                : "<main><a href='/sf/detail/111'>Aimer Arena Live 2099/11/5(木)18:30</a><a href='/sf/detail/222'>Other Artist 2099/12/1(金)20:00</a><a href='/sf/detail/333'>Aimer Past 2000/1/1(土)</a></main>";
            else if (uri.Host.EndsWith("wikipedia.org", StringComparison.Ordinal)) body = "{}";
            else if (uri.AbsolutePath.EndsWith("/artist/", StringComparison.Ordinal)) body = Ambiguous
                ? """{"artists":[{"name":"Aimer","id":"00000000-0000-0000-0000-000000000001"},{"name":"Aimer","id":"00000000-0000-0000-0000-000000000002"}]}"""
                : """{"artists":[{"name":"Aimer","id":"00000000-0000-0000-0000-000000000001"}]}""";
            else if (uri.AbsolutePath.Contains("/event/")) body = EmptyAll ? "{\"events\":[]}" : """{"events":[{"name":"Aimer Live","id":"00000000-0000-0000-0000-000000000002","type":"Concert","life-span":{"begin":"2099-11-04"},"time":"18:00","place":{"name":"Echo Arena"},"area":{"name":"Norway"}},{"name":"Cancelled","id":"00000000-0000-0000-0000-000000000003","cancelled":true,"life-span":{"begin":"2099-11-05"}},{"name":"Awards","id":"00000000-0000-0000-0000-000000000004","type":"Award ceremony","life-span":{"begin":"2099-11-06"}},{"name":"Old","id":"00000000-0000-0000-0000-000000000005","life-span":{"begin":"2000-11-06"}}]}""";
            else body = "{\"name\":\"Aimer\",\"type\":\"Person\",\"relations\":[]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, html ? "text/html" : "application/json") });
        }
    }
}
