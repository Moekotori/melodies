using System.Text.Json;
using Echo.Core.Contracts;

namespace Echo.AlbumInformation;

internal sealed record MusicBrainzResult(AlbumRelease? Release, AlbumRelease[] Versions, ContributorGroup[] Credits,
    InformationLink[] Links, string? ArtistId, double Confidence, bool Ambiguous);
internal sealed class MusicBrainzProvider(MetadataHttp http)
{
    public async Task<MusicBrainzResult?> ReadAsync(AlbumIdentity album, CancellationToken token)
    {
        var title = album.Title.Replace('"', ' ').Replace('\\', ' ').Trim();
        var artist = album.Artist.Replace('"', ' ').Replace('\\', ' ').Trim();
        var query = Uri.EscapeDataString($"release:\"{title}\" AND artist:\"{artist}\"");
        using var search = await http.GetAsync($"https://musicbrainz.org/ws/2/release/?query={query}&fmt=json&limit=8", token).ConfigureAwait(false);
        var ranked = search.RootElement.Field("releases").Items().Select(item => (Item: item, Score: Confidence(item, album))).OrderByDescending(item => item.Score).ToArray();
        if (ranked.Length == 0 || ranked[0].Score < .78) return null;
        var selected = ranked[0]; var id = selected.Item.Text("id");
        if (!Guid.TryParse(id, out _)) return null;
        using var detail = await http.GetAsync($"https://musicbrainz.org/ws/2/release/{id}?fmt=json&inc=artist-credits+labels+recordings+release-groups+url-rels+artist-rels+recording-rels", token).ConfigureAwait(false);
        var root = detail.RootElement;
        var release = ToRelease(root, true);
        var versions = new List<AlbumRelease>();
        var groupId = root.Field("release-group").Text("id");
        if (Guid.TryParse(groupId, out _))
        {
            using var siblings = await http.GetAsync($"https://musicbrainz.org/ws/2/release?release-group={groupId}&fmt=json&inc=artist-credits+labels+media&limit=32", token).ConfigureAwait(false);
            versions.AddRange(siblings.RootElement.Field("releases").Items().Select(item => ToRelease(item, item.Text("id") == id)));
        }
        if (versions.Count == 0) versions.AddRange(ranked.Select(item => ToRelease(item.Item, item.Item.Text("id") == id)));
        var links = new List<InformationLink> { new("MusicBrainz", "发行资料", release.Url, "发行") };
        if (Guid.TryParse(groupId, out _)) links.Add(new("MusicBrainz", "专辑与发行版本", $"https://musicbrainz.org/release-group/{groupId}", "专辑"));
        AddLinks(root, links);
        var groups = new Dictionary<string, List<Contributor>>(StringComparer.OrdinalIgnoreCase);
        AddRelations(root, groups);
        foreach (var medium in root.Field("media").Items().Take(8))
            foreach (var track in medium.Field("tracks").Items().Take(200)) AddRelations(track.Field("recording"), groups);
        var performers = root.Field("artist-credit").Items().Select(credit => credit.Field("artist")).Where(item => item.Text("name") is not null).ToArray();
        if (performers.Length > 0) groups["专辑艺术家"] = performers.Take(12).Select(item => new Contributor(item.Text("name")!, $"https://musicbrainz.org/artist/{item.Text("id")}")).ToList();
        return new(release, versions.DistinctBy(item => item.Id).OrderByDescending(item => item.Selected).ThenBy(item => item.Date).Take(16).ToArray(),
            groups.Take(12).Select(group => new ContributorGroup(group.Key, group.Value.DistinctBy(person => person.Name).Take(16).ToArray())).ToArray(),
            links.DistinctBy(link => link.Url).Take(18).ToArray(), performers.FirstOrDefault().Text("id"), selected.Score,
            ranked.Length > 1 && Math.Abs(ranked[1].Score - selected.Score) < .035);
    }
    private static double Confidence(JsonElement item, AlbumIdentity album)
    {
        var expectedTitle = JsonFields.Normalize(album.Title); var actualTitle = JsonFields.Normalize(item.Text("title") ?? "");
        var expectedArtist = JsonFields.Normalize(album.Artist); var actualArtist = JsonFields.Normalize(item.Artist());
        if (expectedTitle.Length == 0 || actualTitle.Length == 0 || expectedArtist.Length == 0 || actualArtist.Length == 0) return 0;
        var title = actualTitle == expectedTitle ? .55 : actualTitle.Contains(expectedTitle) && expectedTitle.Length >= 4 ? .46 : 0;
        var artist = actualArtist == expectedArtist ? .30 : actualArtist.Contains(expectedArtist) || expectedArtist.Contains(actualArtist) ? .22 : 0;
        // MusicBrainz resolves aliases in its constrained artist search; retain an uncertainty label.
        if (artist == 0 && (item.Number("score") ?? 0) >= 95) artist = .18;
        var year = album.Year is null || item.Text("date")?.StartsWith(album.Year.ToString()!, StringComparison.Ordinal) == true ? .10 : .04;
        var count = item.Field("media").Items().Sum(medium => medium.Number("track-count") ?? 0);
        var tracks = album.TrackCount <= 0 || count == album.TrackCount ? .05 : 0;
        return title + artist + year + tracks;
    }
    private static AlbumRelease ToRelease(JsonElement item, bool selected)
    {
        var media = item.Field("media").Items().ToArray();
        var labels = item.Field("label-info").Items().ToArray();
        var id = item.Text("id") ?? "";
        return new(id, item.Text("title") ?? "未命名发行", item.Artist(), item.Text("date"), item.Text("country"),
            item.Text("status"), item.Text("barcode"), media.Select(row => row.Text("format")).Unique(),
            labels.Select(row => row.Field("label").Text("name")).Unique(), labels.Select(row => row.Text("catalog-number")).Unique(),
            $"https://musicbrainz.org/release/{id}", selected, media.Sum(row => row.Number("track-count") ?? 0));
    }
    private static void AddLinks(JsonElement root, List<InformationLink> links)
    {
        foreach (var relation in root.Field("relations").Items().Take(64))
        {
            var url = relation.Field("url").Text("resource");
            if (JsonFields.SafeLink(url)) links.Add(new(new Uri(url!).Host, relation.Text("type") ?? "外部资料", url!, "外部链接"));
        }
    }
    private static void AddRelations(JsonElement root, Dictionary<string, List<Contributor>> groups)
    {
        foreach (var relation in root.Field("relations").Items().Take(64))
        {
            var artist = relation.Field("artist"); var name = artist.Text("name"); var id = artist.Text("id");
            if (string.IsNullOrWhiteSpace(name) || !Guid.TryParse(id, out _)) continue;
            var role = relation.Text("type") ?? "参与人员";
            role = role switch { "producer" => "制作", "engineer" => "录音工程", "mix" => "混音", "mastering" => "母带", "composer" => "作曲", "lyricist" => "作词", "performer" or "instrument" => "演奏", "vocal" => "演唱", "arranger" => "编曲", _ => role };
            if (!groups.TryGetValue(role, out var people)) { if (groups.Count == 12) continue; groups[role] = people = []; }
            if (people.Count < 32) people.Add(new(name, $"https://musicbrainz.org/artist/{id}", string.Join(" · ", relation.Field("attributes").Items().Select(value => value.GetString()).Unique(8))));
        }
    }
}
