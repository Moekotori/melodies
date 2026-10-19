using Echo.Core.Contracts;

// Deterministic provider data only for rendering and interaction checks.
internal sealed class AlbumOnlineFixture : IAlbumInformationClient
{
    public int Calls { get; private set; }
    public Task<AlbumInformationResult> ReadAsync(AlbumInformationRequest request, CancellationToken cancellation = default)
    {
        Calls++;
        var album = request.Album;
        var release = new AlbumRelease("fixture", album.Title, album.Artist, "2026-05-20", "CN", "Official", "9780000000000",
            ["CD"], ["星河唱片"], ["ECHO-026"], "https://musicbrainz.org", true, album.TrackCount);
        return Task.FromResult(new AlbumInformationResult("ready", DateTimeOffset.UtcNow, ["MusicBrainz", "Wikipedia"], album.Title, album.Artist, .96, false,
            release, [release, release with { Id = "vinyl", Formats = ["Vinyl"], Date = "2026-08-12", Country = "JP", Selected = false }],
            [new("制作", [new("陈小明", "https://musicbrainz.org", "制作人"), new("林雨", "https://musicbrainz.org", "联合制作")]),
             new("演奏", [new("星际乐团", "https://musicbrainz.org", "弦乐 · 钢琴")])],
            [new("MusicBrainz", "发行资料", "https://musicbrainz.org", "发行"), new("Wikipedia", "专辑百科", "https://zh.wikipedia.org", "百科")],
            new(album.Title, "这张专辑以星空与旅途为线索，将钢琴、弦乐与电子音色编织在一起。音乐从安静的开篇展开，经过明亮的节奏段落，最后回到克制而舒缓的尾声。\n\n专辑的录音与制作由不同领域的音乐人共同完成。每一首作品都保持独立的表达，同时连接成一段完整的旅程。", "zh", "https://zh.wikipedia.org"),
            new(album.Artist, "音乐创作人、歌手与制作人，作品融合流行音乐与多种器乐元素。", "zh", "https://zh.wikipedia.org"), []));
    }
}
