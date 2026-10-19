using System.Net;
using System.Text;
using System.Text.Json;
using Echo.AlbumInformation;
using Echo.Core.Contracts;
using Echo.Core.Client;

var cache = Path.Combine(Path.GetTempPath(), "echo-album-info-" + Guid.NewGuid().ToString("N"));
if (args.Contains("--live"))
{
    using var live = new AlbumInformationService(cache);
    var result = await live.ReadAsync(new(args.Contains("--chinese") ? new("七里香", "周杰伦", 2004, 10, "zh") : new("Wish You Were Here", "Pink Floyd", 1975, 5, "en")));
    Console.WriteLine($"Live: {result.Status}; sources={string.Join(',', result.Sources)}; releases={result.Versions.Length}; credits={result.Credits.Length}; albumArticle={result.AlbumArticle is not null}; artistArticle={result.ArtistArticle is not null}");
    foreach (var error in result.Errors) Console.WriteLine(error);
    if ((!args.Contains("--chinese") && result.Release?.Artist != "Pink Floyd") || result.Versions.Length == 0) Environment.ExitCode = 1;
    return;
}
if (args.Contains("--ipc"))
{
    var client = new AlbumInformationClient(Path.GetFullPath("src/Echo.Core.Host/bin/Debug/net10.0"));
    var result = await client.ReadAsync(new(new("Wish You Were Here", "Pink Floyd", 1975, 5, "en")));
    Console.WriteLine($"IPC: {result.Status}; {string.Join(',', result.Sources)}");
    return;
}
var handler = new FixtureHttp();
using var service = new AlbumInformationService(cache, handler);
var request = new AlbumInformationRequest(new("Test Album", "Test Artist", 2020, 12, "en"));
var first = service.ReadAsync(request); var concurrent = service.ReadAsync(request);
var results = await Task.WhenAll(first, concurrent);
Check(handler.Searches == 1, "concurrent requests share provider work");
Check(results[0].Release?.Barcode == "12345" && results[0].Versions.Length == 1, "release details and versions");
Check(results[0].Credits.Any(group => group.Role == "制作"), "contributor relationships");
Check(results[0].AlbumArticle?.Title == "Test Album" && results[0].ArtistArticle?.Title == "Test Artist", "validated album and artist articles");
Check(results[0].Links.All(link => Uri.TryCreate(link.Url, UriKind.Absolute, out var url) && url.Scheme == "https"), "safe source links");
var count = handler.Calls;
await service.ReadAsync(request); Check(handler.Calls == count, "fresh cache avoids network");
handler.Fail = true;
var stale = await service.ReadAsync(request with { Refresh = true });
Check(stale.Status == "stale" && stale.Release is not null, "provider failure preserves cached information");
using var protocol = new MemoryStream();
var envelope = new CoreMetadataRequest(1, Guid.NewGuid().ToString("N"), request);
await CoreMetadataProtocol.WriteAsync(protocol, JsonSerializer.SerializeToUtf8Bytes(envelope, CoreMetadataJson.Default.CoreMetadataRequest), default);
protocol.Position = 0;
Check(JsonSerializer.Deserialize(await CoreMetadataProtocol.ReadAsync(protocol, default), CoreMetadataJson.Default.CoreMetadataRequest)?.Information.Album.Title == "Test Album", "AOT JSON and framed protocol");
Console.WriteLine("PASS album-information: provider matching, release details, contributors, articles, deduplication, cache, offline fallback and protocol.");
static void Check(bool value, string message) { if (!value) throw new Exception(message); }
sealed class FixtureHttp : HttpMessageHandler
{
    public int Calls, Searches; public bool Fail;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        Calls++; if (Fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var url = request.RequestUri!.AbsoluteUri;
        string json;
        if (url.Contains("musicbrainz.org"))
        {
            const string release = """
                {"id":"11111111-1111-1111-1111-111111111111","title":"Test Album","score":100,"date":"2020-01-01","status":"Official","country":"GB","barcode":"12345","artist-credit":[{"name":"Test Artist","artist":{"id":"22222222-2222-2222-2222-222222222222","name":"Test Artist"}}],"release-group":{"id":"33333333-3333-3333-3333-333333333333"},"media":[{"format":"CD","track-count":12}],"label-info":[{"catalog-number":"TEST-01","label":{"name":"Test Label"}}],"relations":[{"type":"producer","artist":{"id":"44444444-4444-4444-4444-444444444444","name":"Producer"}}]}
                """;
            if (url.Contains("query=")) Searches++;
            json = url.Contains("query=") || url.Contains("release-group=") ? "{\"releases\":[" + release + "]}" : release;
        }
        else if (url.Contains("list=search")) json = "{\"query\":{\"search\":[{\"title\":\"" + (url.Contains("srsearch=Test%20Artist") ? "Test Artist" : "Test Album") + "\"}]}}";
        else json = "{\"query\":{\"pages\":[{\"title\":\"" + (url.Contains("Test%20Album") ? "Test Album" : "Test Artist") + "\",\"extract\":\"Test Album is an album by Test Artist.\",\"fullurl\":\"https://en.wikipedia.org/wiki/Test_Album\"}]}}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
