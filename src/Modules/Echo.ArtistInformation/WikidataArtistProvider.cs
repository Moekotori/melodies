using Echo.Contracts.Library;
using System.Text.Json;

namespace Echo.ArtistMetadata;

// Resolve facts only through a matched Wikipedia subject or a confirmed MusicBrainz URL.
internal sealed class WikidataArtistProvider(ArtistHttp http)
{
    public static bool ValidId(string? id) => id is { Length: > 1 and < 16 } && id[0] == 'Q' && id.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;
    public async Task<ArtistPublicFacts?> ReadAsync(string id, string language, CancellationToken token, bool includeDetails = false)
    {
        if (!ValidId(id)) return null;
        const string prefix = "https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&maxlag=5";
        var languages = language == "zh" ? new[] { "zh-hans", "zh", "en" } : new[] { "en", "zh-hans", "zh" };
        using var entity = await http.GetAsync(prefix + "&props=claims|labels|aliases|descriptions&languages=" + string.Join('|', languages) + "&ids=" + id, token).ConfigureAwait(false);
        var subject = entity.RootElement.Field("entities").Field(id);
        if (subject.ValueKind != JsonValueKind.Object || subject.Field("missing").ValueKind != JsonValueKind.Undefined) return null;
        var claims = subject.Field("claims");
        (string Property, string Label)[] fields = [("P106", "职业"), ("P136", "音乐风格"), ("P1303", "乐器"), ("P19", "出生地"), ("P27", "国籍"), ("P495", "国家 / 来源地"), ("P264", "唱片公司")];
        var references = fields.Select(field => (field.Label, Ids: Values(claims, field.Property)
            .Select(claim => Value(claim).Text("id")).Where(ValidId).OfType<string>().Distinct().Take(4).ToArray())).ToArray();
        var members = includeDetails ? new[] { (Property: "P527", Role: "成员"), (Property: "P463", Role: "所属组合") }
            .SelectMany(field => Values(claims, field.Property).Select(claim => (Id: Value(claim).Text("id"), field.Role, Claim: claim)))
            .Where(member => ValidId(member.Id)).Take(16).ToArray() : [];
        var ids = references.SelectMany(field => field.Ids).Concat(members.Select(member => member.Id!)).Distinct().Take(44).ToArray();
        using var labels = ids.Length > 0 ? await http.GetAsync(prefix + "&props=labels&languages=" + string.Join('|', languages) + "&ids=" + string.Join('|', ids), token).ConfigureAwait(false) : null;
        var entities = labels?.RootElement.Field("entities") ?? default;
        string? Label(JsonElement node) => languages.Select(lang => node.Field("labels").Field(lang).Text("value")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var result = new List<ArtistFact>(12);
        foreach (var field in references)
        {
            var names = field.Ids.Select(key => Label(entities.Field(key))).OfType<string>().Distinct().ToArray();
            if (names.Length > 0) result.Add(new(field.Label, string.Join(" / ", names)));
        }
        if (includeDetails)
        {
            foreach (var (property, label) in new[] { ("P2031", "活动开始"), ("P2032", "活动结束"), ("P571", "成立"), ("P576", "解散") })
                if (Values(claims, property).Select(claim => Date(Value(claim))).FirstOrDefault(value => value is not null) is { } date) result.Add(new(label, date));
        }
        var relationships = members.Select(member =>
        {
            var from = Date(Value(member.Claim.Field("qualifiers").Field("P580").Items().FirstOrDefault(), false));
            var to = Date(Value(member.Claim.Field("qualifiers").Field("P582").Items().FirstOrDefault(), false));
            var period = string.Join(" – ", new[] { from, to }.Where(value => value is not null));
            return new ArtistOnlineRelation(Label(entities.Field(member.Id!)) ?? member.Id!, member.Role + (period.Length > 0 ? " · " + period : ""), "https://www.wikidata.org/wiki/" + member.Id);
        }).ToArray();
        var aliases = includeDetails ? languages.SelectMany(lang => subject.Field("aliases").Field(lang).Items())
            .Select(alias => alias.Text("value")).OfType<string>().Distinct().Take(8).ToArray() : [];
        var description = languages.Select(lang => subject.Field("descriptions").Field(lang).Text("value")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return new(Label(subject) ?? id, "https://www.wikidata.org/wiki/" + id, "", aliases, result, relationships, description);
    }
    private static IEnumerable<JsonElement> Values(JsonElement claims, string property) => claims.Field(property).Items().Where(claim => claim.Text("rank") != "deprecated");
    private static JsonElement Value(JsonElement claim, bool main = true) => (main ? claim.Field("mainsnak") : claim).Field("datavalue").Field("value");
    private static string? Date(JsonElement value)
    {
        if (value.Text("time") is not { } time) return null;
        var date = time.TrimStart('+').Split('T')[0];
        return date.EndsWith("-00-00", StringComparison.Ordinal) ? date[..^6] : date.EndsWith("-00", StringComparison.Ordinal) ? date[..^3] : date;
    }
}
