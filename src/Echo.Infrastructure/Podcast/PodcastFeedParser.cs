using System.Globalization;
using System.Text;
using System.Xml;
using Echo.Contracts.Podcast;

namespace Echo.Infrastructure.Podcast;

internal sealed record PodcastDocument(PodcastFeed Feed, PodcastEpisode[] Episodes);

// RSS 2.0 only. No webpage extraction, DTD expansion, or HTML rendering.
internal static partial class PodcastFeedParser
{
    private const string Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private const string Content = "http://purl.org/rss/1.0/modules/content/";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";

    public static PodcastDocument Parse(string xml, string subscriptionUrl, string documentUrl)
    {
        var url = PodcastUrl.Canonical(subscriptionUrl) ?? throw new PodcastFailure("invalid_url");
        if (Encoding.UTF8.GetByteCount(xml) > PodcastLimits.FeedBytes) throw new PodcastFailure("feed_too_large");
        if (xml.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || xml.Contains("<!ENTITY", StringComparison.OrdinalIgnoreCase))
            throw new PodcastFailure("invalid_feed");
        try { return Read(xml, url, string.IsNullOrWhiteSpace(documentUrl) ? url : documentUrl); }
        catch (PodcastFailure) { throw; }
        catch (Exception error) when (error is XmlException or InvalidOperationException) { throw new PodcastFailure("invalid_feed"); }
    }

    private static PodcastDocument Read(string xml, string url, string documentUrl)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true
        });
        var bases = new Stack<string>();
        var names = new Stack<string>();
        bases.Push(documentUrl);
        string? title = null, author = null, description = null, summary = null, link = null, cover = null;
        string? channelBase = null;
        var channelImages = 0;
        Draft? draft = null;
        var window = new List<Draft>(PodcastLimits.Episodes);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var order = 0;
        var feedId = PodcastUrl.Hash(url);
        var buffer = new StringBuilder();
        string? captureLocal = null, captureNs = null, captureParent = null;
        var captureDepth = -1;
        var depth = 0;
        while (reader.Read())
        {
            if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
            {
                if (captureDepth >= 0) buffer.Append(reader.Value);
                continue;
            }
            if (reader.NodeType == XmlNodeType.Element)
            {
                var empty = reader.IsEmptyElement;
                var parent = names.Count == 0 ? "" : names.Peek();
                PushBase(reader, bases);
                var local = reader.LocalName;
                var ns = reader.NamespaceURI;
                names.Push(local);
                depth++;
                if (local == "channel" && ns.Length == 0 && parent == "rss") channelBase = bases.Peek();
                else if (local == "item" && ns.Length == 0 && parent == "channel") draft = new() { Base = bases.Peek() };
                else if (local == "image" && ns.Length == 0 && parent == "channel" && !empty) channelImages++;
                else if (Is(local, ns, "image", Itunes) && parent == "channel")
                    cover ??= PodcastUrl.Canonical(reader.GetAttribute("href"), bases.Peek());
                else if (local == "enclosure" && ns.Length == 0 && draft is not null && draft.Audio is null)
                    draft.Audio = Audio(reader, bases.Peek());
                if (captureDepth < 0 && IsText(local, ns, parent))
                { captureDepth = depth; captureLocal = local; captureNs = ns; captureParent = parent; buffer.Clear(); }
                if (!empty) continue;
            }
            else if (reader.NodeType != XmlNodeType.EndElement) continue;
            if (depth == captureDepth && captureLocal is not null && captureNs is not null && captureParent is not null)
            {
                Apply(captureLocal, captureNs, captureParent, buffer.ToString(), bases.Count == 0 ? documentUrl : bases.Peek(), channelImages > 0,
                    ref title, ref author, ref description, ref summary, ref link, ref cover, draft);
                captureDepth = -1; buffer.Clear();
            }
            var ended = names.Pop();
            var endedBase = bases.Pop();
            if (ended == "item" && names.TryPeek(out var itemParent) && itemParent == "channel" && draft is not null)
            { Keep(window, seen, draft, feedId, order++); draft = null; }
            if (ended == "image" && channelImages > 0) channelImages--;
            if (ended == "channel") channelBase ??= endedBase;
            depth--;
        }
        if (string.IsNullOrEmpty(title)) throw new PodcastFailure("invalid_feed");
        var episodes = Finish(window, title);
        if (episodes.Length == 0) throw new PodcastFailure("no_audio");
        var showBase = channelBase ?? documentUrl;
        var feed = new PodcastFeed(feedId, url, title, First(author), First(description, summary),
            PodcastUrl.Canonical(link, showBase), cover, Stamp(DateTime.UtcNow));
        return new(feed, episodes);
    }

    private static void Apply(string local, string ns, string parent, string raw, string fallback, bool channelImage,
        ref string? title, ref string? author, ref string? description, ref string? summary, ref string? link, ref string? cover, Draft? draft)
    {
        if (parent == "channel")
        {
            if (local == "title" && ns.Length == 0 && title is null) title = Plain(raw, 512);
            else if (Is(local, ns, "author", Itunes) && string.IsNullOrEmpty(author)) author = Plain(raw, 512);
            else if (local == "managingEditor" && ns.Length == 0 && string.IsNullOrEmpty(author)) author = Plain(raw, 512);
            else if (local == "description" && ns.Length == 0 && string.IsNullOrEmpty(description)) description = Plain(raw, 2000);
            else if (Is(local, ns, "summary", Itunes) && string.IsNullOrEmpty(summary)) summary = Plain(raw, 2000);
            else if (local == "link" && ns.Length == 0 && link is null) link = Plain(raw, 4096);
        }
        else if (channelImage && parent == "image" && local == "url" && ns.Length == 0)
            cover ??= PodcastUrl.Canonical(Plain(raw, 4096), fallback);
        else if (draft is not null && parent == "item")
        {
            if (local == "title" && ns.Length == 0 && draft.Title is null) draft.Title = Plain(raw, 512);
            else if (local == "description" && ns.Length == 0 && string.IsNullOrEmpty(draft.Description)) draft.Description = Plain(raw, 2000);
            else if (Is(local, ns, "encoded", Content) && string.IsNullOrEmpty(draft.Encoded)) draft.Encoded = Plain(raw, 2000);
            else if (Is(local, ns, "summary", Itunes) && string.IsNullOrEmpty(draft.Summary)) draft.Summary = Plain(raw, 2000);
            else if (local == "link" && ns.Length == 0 && draft.Link is null) draft.Link = Plain(raw, 4096);
            else if (local == "guid" && ns.Length == 0 && draft.Guid is null) draft.Guid = raw.Trim().Length > 4096 ? raw.Trim()[..4096] : raw.Trim();
            else if (local == "pubDate" && ns.Length == 0 && draft.PublishedAt is null && draft.Rank == 0) draft.Published = Published(Plain(raw, 512));
            else if (Is(local, ns, "duration", Itunes) && draft.Duration is null) draft.Duration = Duration(Plain(raw, 64));
        }
    }

    private static void Keep(List<Draft> window, HashSet<string> seen, Draft draft, string feedId, int order)
    {
        if (draft.Audio is null) return;
        var guid = draft.Guid ?? "";
        var id = "podcast:" + PodcastUrl.Hash(feedId + "\n" + (guid.Length == 0 ? draft.Audio : guid));
        if (!seen.Add(id)) return;
        draft.Id = id; draft.FeedId = feedId; draft.Order = order;
        Retain(window, draft);
    }

    private static PodcastEpisode[] Finish(List<Draft> window, string showTitle)
    {
        var sorted = window.OrderByDescending(item => item.Rank).ThenBy(item => item.Order).ToArray();
        var episodes = new PodcastEpisode[sorted.Length];
        for (var i = 0; i < sorted.Length; i++)
        {
            var item = sorted[i];
            var about = First(item.Description, item.Encoded, item.Summary);
            episodes[i] = new(item.Id!, item.FeedId!, string.IsNullOrEmpty(item.Title) ? showTitle : item.Title,
                about, item.Audio!, PodcastUrl.Canonical(item.Link, item.Base), item.PublishedAt, item.Duration, 0, false);
        }
        return episodes;
    }

    private static string First(params string?[] values)
    {
        foreach (var value in values) if (!string.IsNullOrEmpty(value)) return value;
        return "";
    }

    private static void Retain(List<Draft> heap, Draft candidate)
    {
        if (heap.Count < PodcastLimits.Episodes) { heap.Add(candidate); SiftUp(heap, heap.Count - 1); return; }
        if (Worse(candidate, heap[0]) <= 0) return;
        heap[0] = candidate; SiftDown(heap, 0);
    }

    private static void SiftUp(List<Draft> heap, int index)
    {
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (Worse(heap[index], heap[parent]) >= 0) break;
            (heap[index], heap[parent]) = (heap[parent], heap[index]);
            index = parent;
        }
    }

    private static void SiftDown(List<Draft> heap, int index)
    {
        while (true)
        {
            var left = index * 2 + 1;
            if (left >= heap.Count) break;
            var right = left + 1;
            var child = right < heap.Count && Worse(heap[right], heap[left]) < 0 ? right : left;
            if (Worse(heap[index], heap[child]) <= 0) break;
            (heap[index], heap[child]) = (heap[child], heap[index]);
            index = child;
        }
    }

    private static int Worse(Draft left, Draft right)
    {
        var rank = left.Rank.CompareTo(right.Rank);
        return rank != 0 ? rank : right.Order.CompareTo(left.Order);
    }

    private static void PushBase(XmlReader reader, Stack<string> bases)
    {
        var fallback = bases.Peek();
        var declared = reader.GetAttribute("base", XmlNs);
        bases.Push(string.IsNullOrEmpty(declared) ? fallback : PodcastUrl.Canonical(declared, fallback) ?? fallback);
    }

    private static string? Audio(XmlReader reader, string baseUrl)
    {
        var mime = (reader.GetAttribute("type") ?? "").Trim().ToLowerInvariant().Split(';')[0].Trim();
        if (mime.Length > 0 && !mime.StartsWith("audio/", StringComparison.Ordinal) && mime != "application/ogg") return null;
        return PodcastUrl.Canonical(reader.GetAttribute("url"), baseUrl);
    }

    private static bool IsText(string local, string ns, string parent)
        => parent is "channel" or "item" or "image"
            && (local is "title" or "description" or "link" or "guid" or "pubDate" or "managingEditor" or "url"
                || Is(local, ns, "author", Itunes) || Is(local, ns, "summary", Itunes) || Is(local, ns, "encoded", Content) || Is(local, ns, "duration", Itunes));

    private static bool Is(string local, string ns, string name, string expected) => local == name && ns == expected;

    private static string Plain(string value, int limit)
    {
        var text = value.Length > 32768 ? value[..32768] : value;
        var stripped = Tag().Replace(text, " ");
        return string.Join(' ', stripped.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim() is { Length: > 0 } plain
            ? plain.Length > limit ? plain[..limit] : plain : "";
    }

    private static (string? At, long Rank) Published(string value)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return (null, 0);
        var utc = parsed.UtcDateTime;
        return (Stamp(utc), new DateTimeOffset(utc).ToUnixTimeMilliseconds());
    }

    private static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static double? Duration(string value)
    {
        if (!DurationPattern().IsMatch(value)) return null;
        double seconds = 0;
        foreach (var part in value.Split(':')) seconds = seconds * 60 + double.Parse(part, CultureInfo.InvariantCulture);
        return seconds is > 0 and <= 7 * 86400 ? seconds : null;
    }

    [System.Text.RegularExpressions.GeneratedRegex("<[^>]*>", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex Tag();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\d+(?::\d{1,2}){0,2}(?:\.\d+)?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex DurationPattern();

    private sealed class Draft
    {
        public string? Id, FeedId, Title, Description, Encoded, Summary, Link, Guid, Audio, Base, PublishedAt;
        public double? Duration;
        public long Rank;
        public int Order;
        public (string? At, long Rank) Published { set { PublishedAt = value.At; Rank = value.Rank; } }
    }
}
