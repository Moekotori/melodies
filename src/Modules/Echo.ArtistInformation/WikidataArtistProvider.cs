using Echo.Contracts.Library;

namespace Echo.ArtistMetadata;

// Resolve facts only through the matched Wikipedia subject or a confirmed MusicBrainz URL.
internal sealed class WikidataArtistProvider(ArtistHttp http)
{
    public static bool ValidId(string? id) => id is { Length: > 1 and < 16 } && id[0] == 'Q' && id.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
    public async Task<ArtistFact[]?> ReadAsync(string id, string language, CancellationToken token)
    {
        if (!ValidId(id)) return null;
        const string prefix = "https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&maxlag=5";
        using var entity = await http.GetAsync(prefix + "&props=claims&ids=" + id, token).ConfigureAwait(false);
        var claims = entity.RootElement.Field("entities").Field(id).Field("claims");
        (string Property, string Label)[] fields = [("P106", "职业"), ("P136", "音乐风格"), ("P1303", "乐器"), ("P19", "出生地"), ("P27", "国籍"), ("P495", "起源地区"), ("P264", "唱片公司")];
        var references = fields.Select(field => (field.Label, Ids: claims.Field(field.Property).Items()
            .Where(claim => claim.Text("rank") != "deprecated")
            .Select(claim => claim.Field("mainsnak").Field("datavalue").Field("value").Text("id"))
            .Where(ValidId).OfType<string>().Distinct().Take(4).ToArray())).ToArray();
        var ids = references.SelectMany(field => field.Ids).Distinct().Take(28).ToArray();
        if (ids.Length == 0) return [];
        var languages = language == "zh" ? "zh-hans|zh|en" : "en|zh-hans|zh";
        using var labels = await http.GetAsync(prefix + "&props=labels&languages=" + Uri.EscapeDataString(languages) + "&ids=" + string.Join('|', ids), token).ConfigureAwait(false);
        var entities = labels.RootElement.Field("entities");
        var result = new List<ArtistFact>(7);
        foreach (var field in references)
        {
            var names = field.Ids.Select(key => languages.Split('|').Select(lang => entities.Field(key).Field("labels").Field(lang).Text("value"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))).OfType<string>().Distinct().ToArray();
            if (names.Length > 0) result.Add(new(field.Label, string.Join(" · ", names)));
        }
        return result.ToArray();
    }
}
