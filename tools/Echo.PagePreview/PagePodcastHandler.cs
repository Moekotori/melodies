using System.Net;
using System.Text;

// In-memory transport for populated podcast layouts; never accesses the network.
internal sealed class PagePodcastHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content = new StringContent("""
                <?xml version="1.0" encoding="UTF-8"?>
                <rss version="2.0"><channel>
                <title>夜航电台 · After Hours</title><description>音乐、城市与深夜的故事。</description>
                <link>https://example.com/</link>
                <item><guid>episode-1</guid><title>在城市入睡之后</title><description>沿着音乐继续远行。</description>
                <pubDate>Mon, 19 Oct 2026 12:00:00 GMT</pubDate>
                <enclosure url="https://example.com/episode.mp3" length="1234" type="audio/mpeg" /></item>
                </channel></rss>
                """, Encoding.UTF8, "application/rss+xml")
        });
}
