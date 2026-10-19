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
            using var pages = await http.GetAsync(prefix + "&prop=extracts|pageimages|info&inprop=url&explaintext=1&exintro=1&exchars=6000&piprop=thumbnail&pithumbsize=480&titles=" + Uri.EscapeDataString(string.Join('|', names)), token).ConfigureAwait(false);
            foreach (var page in pages.RootElement.Field("query").Field("pages").Items())
            {
                var title = page.Text("title"); var extract = page.Text("extract");
                if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(extract)) continue;
                var content = JsonFields.Normalize(title + " " + extract);
                if (!content.Contains(JsonFields.Normalize(album.Artist), StringComparison.Ordinal)) continue;
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
}
