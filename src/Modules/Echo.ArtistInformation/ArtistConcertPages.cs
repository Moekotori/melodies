using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Echo.Contracts.Library;

namespace Echo.ArtistMetadata;

// Bounded HTML parsers adapted from ECHO's Eventernote/eplus fallback; no browser or page execution.
internal static partial class ArtistConcertPages
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline;
    [GeneratedRegex("<[^>]+>", Options, 250)] private static partial Regex Tags();
    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant, 250)] private static partial Regex Spaces();
    [GeneratedRegex("<a\\b[^>]*href=[\"'](?<url>[^\"']*/actors/[^\"'?#]+/\\d+)[^\"']*[\"'][^>]*>(?<name>.*?)</a>", Options, 250)] private static partial Regex Actors();
    [GeneratedRegex("<li\\b[^>]*class=[\"']clearfix[^\"']*[\"'][^>]*>.*?(?=<li\\b[^>]*class=[\"']clearfix|</ul>)", Options, 250)] private static partial Regex Blocks();
    [GeneratedRegex("<p\\b[^>]*class=[\"']day\\d*[\"'][^>]*>(?<date>\\d{4}-\\d{2}-\\d{2})", Options, 250)] private static partial Regex Day();
    [GeneratedRegex("<h4[^>]*>\\s*<a\\b[^>]*href=[\"'](?<url>[^\"']+)[\"'][^>]*>(?<title>.*?)</a>", Options, 250)] private static partial Regex EventLink();
    [GeneratedRegex("会場:\\s*<a[^>]*>(?<name>.*?)</a>", Options, 250)] private static partial Regex Venue();
    [GeneratedRegex("開演\\s*[:：]?\\s*(?<time>\\d{1,2}:\\d{2})", Options, 250)] private static partial Regex Starts();
    [GeneratedRegex("<a\\b[^>]*href=[\"'](?<url>[^\"']*/sf/detail/[^\"']+)[\"'][^>]*>(?<body>.*?)</a>", Options, 250)] private static partial Regex Tickets();
    [GeneratedRegex("(?<year>\\d{4})/\\s*(?<month>\\d{1,2})/\\s*(?<day>\\d{1,2})\\([^)]*\\)(?:\\s*(?:開演[:：]\\s*)?(?<time>\\d{1,2}:\\d{2}))?", Options, 250)] private static partial Regex TicketDate();
    [GeneratedRegex("<h1[^>]*>(?<title>.*?)</h1>", Options, 250)] private static partial Regex Heading();
    internal static string Text(string html) => Spaces().Replace(WebUtility.HtmlDecode(Tags().Replace(html, " ")), " ").Trim();
    internal static string? Time(string? value) => TimeOnly.TryParseExact(value?.Trim(), ["H:mm", "HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
    internal static string? SafeUrl(string value, string domain)
    {
        if (!Uri.TryCreate(new Uri("https://" + domain), WebUtility.HtmlDecode(value), out var uri) || uri.Scheme != "https" || uri.Host != domain || uri.Port != 443 || uri.UserInfo.Length > 0) return null;
        return uri.AbsoluteUri;
    }
    internal static string? ActorPage(string html, string name)
    {
        var key = ArtistJson.Key(name);
        var matches = Actors().Matches(html).Cast<Match>().Take(96).Where(match => ArtistJson.Key(Text(match.Groups["name"].Value)) == key)
            .Select(match => SafeUrl(match.Groups["url"].Value, "www.eventernote.com")).OfType<string>().Distinct().Take(2).ToArray();
        return matches.Length == 1 ? matches[0].TrimEnd('/') + "/events" : null;
    }
    internal static IReadOnlyList<ArtistEvent> Eventernote(string html, string name, bool trustedPage)
    {
        var events = new List<ArtistEvent>(12);
        foreach (Match block in Blocks().Matches(html).Cast<Match>().Take(60))
        {
            var date = Day().Match(block.Value).Groups["date"].Value; var link = EventLink().Match(block.Value);
            if (!link.Success || !Future(date) || !trustedPage && !ContainsArtist(Text(block.Value), name)) continue;
            var url = SafeUrl(link.Groups["url"].Value, "www.eventernote.com");
            if (url is null || !new Uri(url).AbsolutePath.StartsWith("/events/", StringComparison.Ordinal)) continue;
            var title = Text(link.Groups["title"].Value); if (title.Length is 0 or > 512) continue;
            if (events.Any(item => item.Url == url)) continue;
            var venue = Text(Venue().Match(block.Value).Groups["name"].Value);
            events.Add(new(title, date, venue.Length > 0 ? venue : null, url, "Eventernote", Time(Starts().Match(block.Value).Groups["time"].Value), Country: "Japan", Timezone: "Asia/Tokyo"));
            if (events.Count == 12) break;
        }
        return events.DistinctBy(item => item.Url).OrderBy(item => item.Date).ThenBy(item => item.Time).ToArray();
    }
    internal static IReadOnlyList<ArtistEvent> Eplus(string html, string name)
    {
        var events = new List<ArtistEvent>(12);
        var artistPage = ArtistJson.Key(Text(Heading().Match(html).Groups["title"].Value)) == ArtistJson.Key(name + " のチケット情報");
        foreach (Match link in Tickets().Matches(html).Cast<Match>().Take(60))
        {
            var body = Text(link.Groups["body"].Value); var date = TicketDate().Match(body);
            if (!date.Success || !artistPage && !ContainsArtist(body, name)) continue;
            var day = $"{date.Groups["year"].Value}-{date.Groups["month"].Value.PadLeft(2, '0')}-{date.Groups["day"].Value.PadLeft(2, '0')}";
            var url = SafeUrl(link.Groups["url"].Value, "eplus.jp"); var title = body[..date.Index].Trim();
            if (!Future(day) || url is null || title.Length is 0 or > 512) continue;
            if (events.Any(item => item.Url == url && item.Date == day && item.Time == Time(date.Groups["time"].Value))) continue;
            events.Add(new(title, day, null, url, "eplus", Time(date.Groups["time"].Value), Country: "Japan", TicketUrl: url, Timezone: "Asia/Tokyo"));
            if (events.Count == 12) break;
        }
        return events.DistinctBy(item => (item.Url, item.Date, item.Time)).OrderBy(item => item.Date).ThenBy(item => item.Time).ToArray();
    }
    private static bool Future(string date) => DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day >= DateOnly.FromDateTime(DateTime.UtcNow);
    private static bool ContainsArtist(string text, string name)
    {
        // Preserve word boundaries for Latin names, while retaining Japanese and Chinese matching.
        var normalized = text.Normalize(System.Text.NormalizationForm.FormKC); var needle = name.Normalize(System.Text.NormalizationForm.FormKC);
        return Regex.IsMatch(normalized, "(?<![\\p{L}\\p{N}])" + Regex.Escape(needle) + "(?![\\p{L}\\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
