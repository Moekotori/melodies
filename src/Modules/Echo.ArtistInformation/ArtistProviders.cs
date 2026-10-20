using Echo.Contracts.Library;
using System.Globalization;

namespace Echo.ArtistMetadata;

internal sealed class ArtistProviders(ArtistHttp http)
{
    public async Task<ArtistBiography?> BiographyAsync(ArtistInformationRequest request, CancellationToken token)
    {
        Exception? failure = null; var succeeded = false;
        foreach (var language in request.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? new[] { "zh", "en" } : new[] { "en", "zh" })
        {
            try
            {
                var prefix = $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2" + (language == "zh" ? "&variant=zh-cn&converttitles=1" : "");
                using var search = await http.GetAsync(prefix + "&list=search&srlimit=3&srsearch=" + Uri.EscapeDataString(request.Name), token).ConfigureAwait(false);
                succeeded = true;
                var names = search.RootElement.Field("query").Field("search").Items().Select(item => item.Text("title")).OfType<string>().Append(request.Name).Distinct().Take(4);
                using var result = await http.GetAsync(prefix + "&redirects=1&prop=extracts|info|pageprops|pageimages&piprop=thumbnail&pithumbsize=960&inprop=url&explaintext=1&exchars=2800&titles=" + Uri.EscapeDataString(string.Join('|', names)), token).ConfigureAwait(false);
                var query = result.RootElement.Field("query");
                var aliases = query.Field("normalized").Items().Concat(query.Field("converted").Items()).Concat(query.Field("redirects").Items()).Take(16).ToArray();
                string Canonical(string title)
                {
                    for (var step = 0; step < 8; step++)
                    { var next = aliases.FirstOrDefault(alias => alias.Text("from") == title).Text("to"); if (next is null || next == title) break; title = next; }
                    var suffix = title.IndexOfAny(['(', '（']); return ArtistJson.Key(suffix < 0 ? title : title[..suffix]);
                }
                var candidates = query.Field("pages").Items().Where(page => page.Text("extract") is { Length: > 0 } && Canonical(page.Text("title") ?? "") == Canonical(request.Name)).Take(4).ToArray();
                // A name alone must not pick one of several disambiguated subjects.
                if (candidates.Length != 1) continue;
                var page = candidates[0]; var text = page.Text("extract")!;
                if (text.Contains("可能指", StringComparison.Ordinal) || text.Contains("may refer to", StringComparison.OrdinalIgnoreCase)) continue;
                var lead = text[..Math.Min(450, text.Length)];
                if (!new[] { "歌手", "音乐", "音樂", "作曲", "乐队", "樂隊", "指挥", "指揮", "艺人", "藝人", "singer", "musician", "composer", "band", "rapper", "orchestra", "disc jockey" }
                    .Any(term => lead.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                return new(page.Text("title")!, text[..Math.Min(2400, text.Length)], language, page.Text("fullurl") ?? $"https://{language}.wikipedia.org/wiki/{Uri.EscapeDataString(page.Text("title")!)}", page.Field("pageprops").Text("wikibase_item"), ThumbnailUrl: page.Field("thumbnail").Text("source"));
            }
            catch (Exception error) when (error is HttpRequestException or IOException or System.Text.Json.JsonException) { failure = error; }
        }
        if (!succeeded && failure is not null) throw new HttpRequestException("Wikipedia unavailable.", failure);
        return null;
    }

    public async Task<ArtistInformation?> MusicAsync(ArtistInformationRequest request, CancellationToken token)
    {
        var escaped = request.Name.Replace('"', ' ').Replace('\\', ' ');
        using var search = await http.GetAsync("https://musicbrainz.org/ws/2/artist/?fmt=json&limit=8&query=" + Uri.EscapeDataString($"artist:\"{escaped}\""), token).ConfigureAwait(false);
        var key = ArtistJson.Key(request.Name);
        var matches = search.RootElement.Field("artists").Items().Where(item => ArtistJson.Key(item.Text("name") ?? "") == key ||
            item.Field("aliases").Items().Any(alias => ArtistJson.Key(alias.Text("name") ?? "") == key)).Take(2).ToArray();
        if (matches.Length != 1 || !Guid.TryParse(matches[0].Text("id"), out var id)) return null;
        using var detail = await http.GetAsync($"https://musicbrainz.org/ws/2/artist/{id}?fmt=json&inc=url-rels+artist-rels", token).ConfigureAwait(false);
        var root = detail.RootElement; var facts = new List<ArtistFact>(6);
        void Add(string label, string? value) { if (!string.IsNullOrWhiteSpace(value)) facts.Add(new(label, value)); }
        Add("类型", root.Text("type") switch { "Person" => "音乐人", "Group" => "乐队 / 组合", "Orchestra" => "管弦乐团", "Choir" => "合唱团", _ => root.Text("type") });
        Add("地区", root.Field("area").Text("name") ?? root.Text("country"));
        Add(root.Text("type") == "Person" ? "出生日期" : "成立日期", root.Field("life-span").Text("begin"));
        Add(root.Text("type") == "Person" ? "逝世日期" : "结束日期", root.Field("life-span").Text("end"));
        Add("消歧义说明", root.Text("disambiguation"));
        var links = new List<ArtistLink> { new("MusicBrainz", $"https://musicbrainz.org/artist/{id}") };
        var relations = new List<ArtistOnlineRelation>(12);
        foreach (var relation in root.Field("relations").Items().Take(96))
        {
            var url = relation.Field("url").Text("resource");
            if (ArtistJson.SafeLink(url)) links.Add(new(relation.Text("type") ?? new Uri(url!).Host, url!));
            var person = relation.Field("artist");
            if (person.Text("name") is { } name && Guid.TryParse(person.Text("id"), out var relatedId) && relations.Count < 12)
                relations.Add(new(name, relation.Text("type") == "member of band" ? relation.Text("direction") == "backward" ? "成员" : "所属组合" : relation.Text("type") ?? "关联艺人", $"https://musicbrainz.org/artist/{relatedId}"));
        }
        // Event failure must preserve identity, facts and relationships already fetched.
        var errors = new List<string>(); var events = new List<ArtistEvent>(12);
        try
        {
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var query = Uri.EscapeDataString($"arid:{id} AND begin:[{today} TO *]");
            using var result = await http.GetAsync($"https://musicbrainz.org/ws/2/event/?fmt=json&limit=12&query={query}", token).ConfigureAwait(false);
            foreach (var item in result.RootElement.Field("events").Items().Take(12))
            {
                if (item.Field("cancelled").ValueKind == System.Text.Json.JsonValueKind.True || item.Text("type") is { } type && type is not ("Concert" or "Festival" or "Stage performance")) continue;
                var date = item.Field("life-span").Text("begin");
                if (item.Text("name") is { } name && Guid.TryParse(item.Text("id"), out var eventId) &&
                    DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day >= DateOnly.FromDateTime(DateTime.UtcNow))
                    events.Add(new(name, date!, item.Field("place").Text("name") ?? item.Field("relations").Items().Select(r => r.Field("place").Text("name")).FirstOrDefault(n => n is not null), $"https://musicbrainz.org/event/{eventId}",
                        Time: ArtistConcertPages.Time(item.Text("time")), Region: item.Field("area").Text("name"), Country: item.Field("area").Field("iso-3166-1-codes").Items().FirstOrDefault().GetStringOrNull()));
            }
        }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
        { errors.Add("MusicBrainz 活动暂时无法读取，其他资料仍可查看。"); }
        return new(errors.Count > 0 ? "partial" : "ready", DateTimeOffset.UtcNow, null, facts, relations, events.OrderBy(e => e.Date).ToArray(), links.DistinctBy(l => l.Url).ToArray(), errors, errors.Count > 0 ? "error" : "ready");
    }
}
