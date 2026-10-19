using System.Text.Json;
using Echo.Core.Contracts;

namespace Echo.AlbumInformation;

internal sealed class WikipediaProvider(MetadataHttp http)
{
    public async Task<InformationArticle?> ReadAsync(AlbumIdentity album, bool artist, CancellationToken token)
    {
        var languages = album.Language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? new[] { "zh", "en" } : new[] { "en", "zh" };
        Exception? failure = null; var succeeded = false;
        foreach (var language in languages)
        {
            try
            {
            var keyword = artist ? album.Artist : album.Title + " " + album.Artist;
            var prefix = $"https://{language}.wikipedia.org/w/api.php?action=query&format=json&formatversion=2" + (language == "zh" ? "&variant=zh-cn&converttitles=1" : "");
            using var search = await http.GetAsync(prefix + "&list=search&srlimit=3&srsearch=" + Uri.EscapeDataString(keyword), token).ConfigureAwait(false);
            succeeded = true;
            var names = search.RootElement.Field("query").Field("search").Items().Select(item => item.Text("title")).Unique(3);
            if (names.Length == 0) continue;
            var requestedTitle = artist ? album.Artist : album.Title;
            using var pages = await http.GetAsync(prefix + "&redirects=1&prop=extracts|pageimages|info&inprop=url&explaintext=1&exintro=1&exchars=6000&piprop=thumbnail&pithumbsize=480&titles=" + Uri.EscapeDataString(string.Join('|', names.Append(requestedTitle).Distinct(StringComparer.OrdinalIgnoreCase))), token).ConfigureAwait(false);
            // Multi-page extracts need not follow search order. Require the page's subject,
            // not a passing mention in another album's or artist's introduction.
            var query = pages.RootElement.Field("query");
            var aliases = query.Field("normalized").Items().Concat(query.Field("converted").Items()).Concat(query.Field("redirects").Items()).Take(16).ToArray();
            var subject = Subject(CanonicalTitle(requestedTitle, aliases));
            foreach (var page in query.Field("pages").Items()
                .OrderBy(page => { var rank = Array.FindIndex(names, name => string.Equals(name, page.Text("title"), StringComparison.OrdinalIgnoreCase)); return rank < 0 ? int.MaxValue : rank; }))
            {
                var title = page.Text("title"); var extract = page.Text("extract");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(extract)) continue;
                if (Subject(CanonicalTitle(title, aliases)) != subject) continue;
                var content = JsonFields.Normalize(title + " " + extract);
                if (!artist && !content.Contains(JsonFields.Normalize(album.Artist), StringComparison.Ordinal)) continue;
                if (!artist && !content.Contains(JsonFields.Normalize(album.Title), StringComparison.Ordinal)) continue;
                var url = page.Text("fullurl") ?? $"https://{language}.wikipedia.org/wiki/{Uri.EscapeDataString(title)}";
                return new(title, extract[..Math.Min(6000, extract.Length)], language, url, ImageUrl: page.Field("thumbnail").Text("source"));
            }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { failure = new HttpRequestException("Wikipedia request timed out."); }
            catch (Exception error) when (error is HttpRequestException or IOException or System.Text.Json.JsonException) { failure = error; }
        }
        if (!succeeded && failure is not null) throw new HttpRequestException("Wikipedia is unavailable.", failure);
        return null;
    }
    private static string Subject(string title)
    {
        var suffix = title.IndexOfAny(['(', '（']);
        return JsonFields.Normalize(suffix < 0 ? title : title[..suffix]);
    }
    private static string CanonicalTitle(string title, JsonElement[] aliases)
    {
        for (var step = 0; step < 8; step++)
        {
            var target = aliases.FirstOrDefault(alias => string.Equals(alias.Text("from"), title, StringComparison.OrdinalIgnoreCase)).Text("to");
            if (target is null || target == title) break;
            title = target;
        }
        return title;
    }
}
