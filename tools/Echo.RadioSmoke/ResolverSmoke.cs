using System.Net;
using Echo.Infrastructure.Radio;

internal static class ResolverSmoke
{
    public static async Task RunAsync(CancellationToken token)
    {
        using var resolver = new HttpRadioStreamResolver(new Fixture());
        var pls = await resolver.ResolveAsync("https://radio.test/list.pls", token);
        Check(pls.Url == "https://radio.test/audio?token=example" && pls.Codec == "AAC" && pls.BitrateKbps == 128, "PLS / redirect / audio headers");
        var m3u = await resolver.ResolveAsync("https://radio.test/list.m3u", token);
        Check(m3u.Url == "https://radio.test/audio?token=example", "Relative M3U with query parameters");
        var hls = await resolver.ResolveAsync("https://radio.test/live.m3u8", token);
        Check(hls.Codec == "HLS" && hls.Url.EndsWith("live.m3u8"), "HLS must stay a manifest");
        foreach (var path in new[] { "page", "loop.m3u", "huge.m3u", "credentials" })
        {
            try { await resolver.ResolveAsync("https://radio.test/" + path, token); throw new InvalidOperationException("Invalid stream accepted: " + path); }
            catch (IOException) { }
        }
        Console.WriteLine("PASS bounded resolver: PLS/M3U/HLS, redirects, headers, HTML rejection, loops and byte limit");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Fixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
            var text = path switch
            {
                "/list.pls" => "[playlist]\nNumberOfEntries=1\nFile1=/redirect\nTitle1=Station\n",
                "/list.m3u" => "#EXTM3U\n#EXTINF:-1,Station\n/audio?token=example\n",
                "/live.m3u8" => "#EXTM3U\n#EXT-X-TARGETDURATION:5\n#EXTINF:5,\nsegment.aac\n",
                "/loop.m3u" => "#EXTM3U\n/loop.m3u\n", "/huge.m3u" => new string('x', 256 * 1024 + 1),
                "/page" => "<html>not a stream</html>", _ => ""
            };
            response.Content = new StringContent(text);
            response.Content.Headers.ContentType = new(path == "/page" ? "text/html" : path.EndsWith(".pls") ? "audio/x-scpls" : path.Contains(".m3u") ? "audio/x-mpegurl" : "audio/aac");
            if (path is "/redirect" or "/credentials")
            { response.StatusCode = HttpStatusCode.Redirect; response.Headers.Location = new(path == "/redirect" ? "/audio?token=example" : "https://user:password@radio.test/audio", UriKind.RelativeOrAbsolute); }
            response.Headers.TryAddWithoutValidation("icy-br", "128");
            return Task.FromResult(response);
        }
    }
}
