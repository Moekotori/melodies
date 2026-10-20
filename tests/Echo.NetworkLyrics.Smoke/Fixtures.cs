using System.Net;
using System.Text;

static class Fixtures
{
    public static Echo.Lyrics.LyricsIdentity Identity { get; } = new("Song", "Singer", "Album", 180);
    public static HttpResponseMessage Response(HttpRequestMessage request)
    {
        var uri = request.RequestUri!;
        var value = uri.Host switch
        {
            "api.amll.dev" when uri.AbsolutePath.EndsWith("search", StringComparison.Ordinal) => """
                {"status":200,"data":{"items":[{"id":1,"musicNames":["Song"],"artistNames":["Singer"],"ncmMusicIds":["42"]}]}}
                """,
            "api.amll.dev" => """
                {"status":200,"data":{"id":1,"musicNames":["Song"],"artistNames":["Singer"],"ncmMusicIds":["42"],"lyrics":"<tt xmlns=\"http://www.w3.org/ns/ttml\"><body><div><p begin=\"1s\" end=\"3s\"><span begin=\"1s\" end=\"2s\">hello </span><span begin=\"2s\" end=\"3s\">world</span></p></div></body></tt>"}}
                """,
            "music.163.com" when uri.AbsolutePath.Contains("search", StringComparison.Ordinal) => """
                {"code":200,"result":{"songs":[{"id":42,"name":"Song","artists":[{"name":"Singer"}],"album":{"name":"Album"},"duration":180000}]}}
                """,
            "music.163.com" => """
                {"code":200,"lrc":{"lyric":"[00:01.00]hello world"},"yrc":{"lyric":"[1000,2000](1000,1000,0)hello (2000,1000,0)world"},"tlyric":{"lyric":"[00:01.00]你好"}}
                """,
            "u.y.qq.com" => """
                {"req_1":{"data":{"body":{"song":{"list":[{"mid":"qq1","name":"Song","singer":[{"name":"Singer"}],"album":{"name":"Album"},"interval":180}]}}}}}
                """,
            "c.y.qq.com" => """
                {"code":0,"lyric":"WzAwOjAxLjAwXWhlbGxvIHdvcmxk","trans":"[00:01.00]你好"}
                """,
            "lrclib.net" => """
                [{"id":1,"trackName":"Song","artistName":"Singer","albumName":"Album","duration":180,"syncedLyrics":"[00:01.00]hello world"},
                 {"id":2,"trackName":"Song","artistName":"Wrong singer","duration":180,"syncedLyrics":"[00:01.00]wrong"}]
                """,
            _ => throw new Exception("Unexpected provider domain")
        };
        return Json(value);
    }
    public static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
}

sealed class FixtureHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
}
