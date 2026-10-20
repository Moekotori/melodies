using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Artists;

// Same source selection as ECHO: metadata, official sites and named social accounts.
internal static class ArtistSourceLinks
{
    private static readonly Dictionary<string, string> Metadata = new()
    {
        ["wikipedia.org"] = "Wikipedia", ["musicbrainz.org"] = "MusicBrainz", ["wikidata.org"] = "Wikidata",
        ["discogs.com"] = "Discogs", ["allmusic.com"] = "AllMusic", ["baike.baidu.com"] = "百度百科", ["zh.moegirl.org.cn"] = "萌娘百科"
    };
    private static readonly Dictionary<string, string> Social = new()
    {
        ["twitter.com"] = "X", ["x.com"] = "X", ["instagram.com"] = "Instagram", ["facebook.com"] = "Facebook",
        ["weibo.com"] = "微博", ["weibo.cn"] = "微博", ["bsky.app"] = "Bluesky", ["threads.net"] = "Threads",
        ["threads.com"] = "Threads", ["tiktok.com"] = "TikTok", ["myspace.com"] = "Myspace", ["youtube.com"] = "YouTube",
        ["soundcloud.com"] = "SoundCloud", ["bandcamp.com"] = "Bandcamp", ["bilibili.com"] = "哔哩哔哩"
    };
    internal static IReadOnlyList<ArtistLink> Present(IReadOnlyList<ArtistLink>? links)
    {
        var result = new List<ArtistLink>(); var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var socialUrls = new HashSet<string>(StringComparer.Ordinal); var sourceCount = 0;
        foreach (var link in (links ?? []).Take(128))
        {
            if (!Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0) continue;
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            var metadata = Metadata.FirstOrDefault(pair => Matches(host, pair.Key)).Value;
            if (link.Label.Contains("fan", StringComparison.OrdinalIgnoreCase)) continue;
            var platform = Social.FirstOrDefault(pair => Matches(host, pair.Key)).Value;
            var social = platform is not null || link.Label.Equals("social network", StringComparison.OrdinalIgnoreCase) || link.Label.Equals("myspace", StringComparison.OrdinalIgnoreCase) || link.Label.Equals("youtube", StringComparison.OrdinalIgnoreCase);
            var official = link.Label.Contains("official", StringComparison.OrdinalIgnoreCase) || link.Label.Contains("homepage", StringComparison.OrdinalIgnoreCase);
            if (metadata is null && !social && !official) continue;
            var label = metadata ?? (social ? platform ?? host : "Official");
            if (social) { if (socialUrls.Count >= 24 || !socialUrls.Add(uri.AbsoluteUri.TrimEnd('/'))) continue; }
            else { if (sourceCount >= 6 || !labels.Add(label)) continue; sourceCount++; }
            result.Add(link with { Label = label });
        }
        return result;
    }
    internal static bool IsSocial(ArtistLink link) => !Metadata.Values.Contains(link.Label) && link.Label != "Official";
    internal static IReadOnlyList<ArtistSocialAccount> Accounts(IReadOnlyList<ArtistLink> links) => links.Where(IsSocial).Select(link =>
    {
        var uri = new Uri(link.Url); var path = Uri.UnescapeDataString(uri.AbsolutePath).Trim('/');
        var handle = path.StartsWith("profile/", StringComparison.Ordinal) ? path[8..] : path;
        if (path == "profile.php" && uri.Query.Length > 1) handle = uri.Query[1..];
        if (link.Label == "Bandcamp" && uri.Host.EndsWith(".bandcamp.com", StringComparison.Ordinal)) handle = uri.Host[..^13];
        if (handle.Length == 0) handle = uri.Host;
        else if (!handle.StartsWith('@') && !handle.Contains('/')) handle = "@" + handle;
        return new ArtistSocialAccount(link.Label, handle, link.Url);
    }).ToArray();
    internal static string Providers(IReadOnlyList<ArtistLink> links) => string.Join(" / ", links.Select(link => link.Label)
        .Where(label => label is "Wikipedia" or "MusicBrainz" or "Wikidata" or "百度百科" or "萌娘百科").Distinct());
    private static bool Matches(string host, string domain) => host == domain || host.EndsWith('.' + domain, StringComparison.OrdinalIgnoreCase);
}
public sealed record ArtistSocialAccount(string Platform, string Account, string Url);
