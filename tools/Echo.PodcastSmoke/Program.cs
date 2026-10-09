using System.Globalization;
using System.Text;
using Echo.Application.Podcast;
using Echo.Contracts.Podcast;
using Echo.Infrastructure.Podcast;

var url = "https://podcast.example/feed.xml";
string Rss(string items) => $"""<rss version="2.0" xmlns:itunes="http://www.itunes.com/dtds/podcast-1.0.dtd"><channel><title>Show</title><itunes:author>Author</itunes:author>{items}</channel></rss>""";
string Item(string guid = "episode-1", string audio = "/episode.mp3") => $"""<item><guid>{guid}</guid><title>Episode</title><description><![CDATA[<b>About</b> this episode]]></description><enclosure url="{audio}" type="audio/mpeg"/><itunes:duration>1:02:03</itunes:duration></item>""";

var namespaced = Rss(Item().Replace("<description><![CDATA[<b>About</b> this episode]]></description>", """<body:encoded><![CDATA[<p>Full episode notes</p>]]></body:encoded>""", StringComparison.Ordinal))
    .Replace("<enclosure", """<enclosure url="/video.mp4" type="video/mp4"/><enclosure""", StringComparison.Ordinal)
    .Replace("<rss ", """<rss xmlns:body="http://purl.org/rss/1.0/modules/content/" """, StringComparison.Ordinal)
    .Replace("<title>Show</title>", "<title>Show</title><itunes:summary>Show summary</itunes:summary>", StringComparison.Ordinal);
var parsed = PodcastFeedParser.Parse(namespaced, url, url);
Check(parsed.Feed.Description == "Show summary", "show summary");
Check(parsed.Episodes[0].AudioUrl == "https://podcast.example/episode.mp3" && parsed.Episodes[0].Description == "Full episode notes", "audio after video");

var aliased = Rss(Item("raw&lt;guid&gt;")).Replace("itunes:", "pod:", StringComparison.Ordinal).Replace("xmlns:itunes=", "xmlns:pod=", StringComparison.Ordinal)
    .Replace("<channel>", """<channel xml:base="https://cdn.example/shows/">""", StringComparison.Ordinal)
    .Replace("<item>", """<item xml:base="episodes/">""", StringComparison.Ordinal)
    .Replace("url=\"/episode.mp3\"", "url=\"episode.mp3\"", StringComparison.Ordinal);
var aliasedEpisode = PodcastFeedParser.Parse(aliased, url, url).Episodes[0];
Check(aliasedEpisode.AudioUrl == "https://cdn.example/shows/episodes/episode.mp3", "xml:base");
Check(aliasedEpisode.Duration == 3723, "duration");
Check(aliasedEpisode.Id != PodcastFeedParser.Parse(Rss(Item("raw")), url, url).Episodes[0].Id, "guid identity");

var first = PodcastFeedParser.Parse(Rss(Item()), url, "https://cdn.example/show/feed.xml");
var refreshed = PodcastFeedParser.Parse(Rss(Item("episode-1", "/new.mp3")), url, url);
Check(first.Episodes[0].AudioUrl == "https://cdn.example/episode.mp3" && first.Episodes[0].Description == "About this episode", "relative audio");
Check(first.Episodes[0].Id == refreshed.Episodes[0].Id, "stable guid");

Expect("invalid_feed", () => PodcastFeedParser.Parse("<!DOCTYPE rss [<!ENTITY x \"test\">]>" + Rss(Item()), url, url));
Expect("invalid_feed", () => PodcastFeedParser.Parse("<rss><channel><title>X</title></rss>", url, url));
Expect("invalid_feed", () => PodcastFeedParser.Parse("<html><body>Show</body></html>", url, url));
Expect("no_audio", () => PodcastFeedParser.Parse(Rss(Item().Replace("audio/mpeg", "video/mp4", StringComparison.Ordinal)), url, url));
Expect("no_audio", () => PodcastFeedParser.Parse(Rss(Item("x", "https://user:password@example.test/x")), url, url));
Expect("no_audio", () => PodcastFeedParser.Parse(Rss(Item("x", "file:///example.mp3")), url, url));
Check(PodcastFeedParser.Parse(Rss(Item() + Item()), url, url).Episodes.Length == 1, "duplicate guid");
Check(PodcastFeedParser.Parse(Rss(string.Concat(Enumerable.Range(0, 501).Select(index => Item(index.ToString())))), url, url).Episodes.Length == 500, "episode cap");
Expect("feed_too_large", () => PodcastFeedParser.Parse(new string(' ', PodcastLimits.FeedBytes + 1), url, url));

var dated = string.Concat(Enumerable.Range(0, 501).Select(index => Item(index.ToString()).Replace("</item>",
    $"<pubDate>{DateTimeOffset.FromUnixTimeMilliseconds(index * 86_400_000L).ToString("R", CultureInfo.InvariantCulture)}</pubDate></item>", StringComparison.Ordinal)));
var window = PodcastFeedParser.Parse(Rss(dated), url, url);
Check(window.Episodes.Length == 500, "newest window size");
Check(window.Episodes[0].PublishedAt == Stamp(500) && window.Episodes[^1].PublishedAt == Stamp(1), "newest window order");

var invalid = new byte[] { 0xFF, 0xFE, 0x00 };
try { PodcastFeedDecoder.Decode(invalid, null); Check(false, "utf-16"); }
catch (PodcastFailure error) { Check(error.Code == "invalid_feed", "bad encoding"); }
var declared = PodcastFeedDecoder.Decode("""<?xml version="1.0" encoding="iso-8859-1"?><rss/>"""u8, null);
Check(declared.Contains("iso-8859-1", StringComparison.Ordinal), "xml encoding");

var root = Path.Combine(Path.GetTempPath(), "echo-podcast-smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var database = new PodcastDatabase(Path.Combine(root, "podcasts.sqlite"));
try
{
    string StoreRss(string audio = "https://podcast.example/audio.mp3", int count = 1) => "<rss><channel><title>Show</title>" + string.Concat(Enumerable.Range(0, count).Select(index => $"<item><guid>{index}</guid><title>Episode {index}</title><enclosure url=\"{audio}\" type=\"audio/mpeg\"/></item>")) + "</channel></rss>";
    var feed = database.Import(StoreRss(count: 85), "https://podcast.example/feed", "https://podcast.example/feed", null, null);
    var old = database.Episodes(new(feed.Id, 2)).Items[0];
    var pageFirst = database.Episodes(new(feed.Id)).Items[0];
    database.Progress(old.Id, 300, false);
    database.MarkPlayed(pageFirst.Id, true);
    var progress = database.Episodes(new(feed.Id, Filter: PodcastEpisodeFilter.InProgress));
    Check(progress.Total == 1 && progress.Items[0].Id == old.Id && progress.Items[0].Position == 300, "in progress beyond page");
    Check(database.Episodes(new(feed.Id, Filter: PodcastEpisodeFilter.Unplayed)).Total == 84, "unplayed");
    Check(database.Episodes(new(feed.Id, Search: pageFirst.Title, Filter: PodcastEpisodeFilter.Played)).Items[0].Id == pageFirst.Id, "played search");
    Check(database.Episodes(new(feed.Id, Search: pageFirst.Title, Filter: PodcastEpisodeFilter.InProgress)).Total == 0, "search excludes other state");
    var ordered = database.Import(StoreRss(count: 3), "https://podcast.example/order", "https://podcast.example/order", null, null);
    Check(string.Join(',', database.Episodes(new(ordered.Id)).Items.Select(item => item.Title)) == "Episode 0,Episode 1,Episode 2", "publisher order");
    database.Import(StoreRss(count: 3).Replace("</item>", "<pubDate>Wed, 01 Jul 2026 00:00:00 GMT</pubDate></item>", StringComparison.Ordinal), ordered.Url, ordered.Url, null, null);
    Check(string.Join(',', database.Episodes(new(ordered.Id)).Items.Select(item => item.Title)) == "Episode 0,Episode 1,Episode 2", "equal dates");

    var validators = new PodcastValidators("\"first\"", null);
    var stable = database.Import(StoreRss(), "https://podcast.example/stable", "https://podcast.example/stable", null, validators);
    var before = database.Changes();
    var again = database.Import(StoreRss(), stable.Url, stable.Url, stable.Id, validators);
    Check(again.UpdatedAt == stable.UpdatedAt && database.Changes() == before, "identical refresh");
    database.NotModified(stable.Id, validators);
    Check(database.Changes() == before, "identical validators");
    var episode = database.Episodes(new(stable.Id)).Items[0];
    database.Progress(episode.Id, 20, false);
    var saved = database.Changes();
    database.Progress(episode.Id, 20, false);
    Check(database.Changes() == saved, "identical progress");
    Check(database.Import(StoreRss("https://podcast.example/new.mp3"), stable.Url, stable.Url, null, null).UpdatedAt == stable.UpdatedAt, "artwork identity");

    database.Remove(stable.Id);
    Expect("not_found", () => database.Import(StoreRss(), stable.Url, stable.Url, stable.Id, null));
    Expect("not_found", () => database.NotModified(stable.Id, validators));

    var kept = database.Import(StoreRss(), "https://podcast.example/kept", "https://podcast.example/kept", null, null);
    var moving = database.Episode(old.Id);
    Check(moving?.Position == 300, "other feed untouched");
    var primary = database.Episodes(new(kept.Id)).Items[0];
    database.Progress(primary.Id, 91, false);
    database.Import(StoreRss("https://podcast.example/new.mp3"), kept.Url, kept.Url, null, null);
    var moved = database.Episode(primary.Id)!;
    Check(moved.Position == 91 && !moved.Played && moved.AudioUrl == "https://podcast.example/new.mp3", "progress survives refresh");
    database.Progress(primary.Id, 100, true);
    Check(database.Episode(primary.Id)!.Played, "ended marks played");
    database.MarkPlayed(primary.Id, false);
    var cleared = database.Episode(primary.Id)!;
    Check(!cleared.Played && cleared.Position == 0, "mark unplayed");
    database.Remove(kept.Id);
    Check(database.Episode(primary.Id) is null, "unsubscribe removes episodes");
    Check(database.Episodes(new(feed.Id)).Items.Count == 40 && database.Episodes(new(feed.Id, 2)).Items.Count == 5, "page size");
    Check(database.Episodes(new(feed.Id, Search: "Episode 84")).Items.Count == 1, "episode search");
    Check(database.Episodes(new(feed.Id, Search: "%")).Total == 0, "escaped search");
    Expect("invalid_feed", () => database.Import("<html/>", feed.Url, feed.Url, null, null));
    Check(database.Episodes(new(feed.Id)).Total == 85, "invalid refresh keeps feed");
}
finally { database.Dispose(); Directory.Delete(root, true); }

await ProgressAsync();
Console.WriteLine("PASS podcast RSS, SQLite progress and playback samples");

static async Task ProgressAsync()
{
    var saved = new List<PodcastProgressSample>();
    long now = 0;
    var tracker = new PodcastProgressTracker(sample => { saved.Add(sample); return Task.CompletedTask; }, () => now);
    tracker.Observe("podcast:a", "playing", 1);
    tracker.Observe("podcast:a", "playing", 90);
    tracker.Observe("music", "playing", 0);
    await tracker.FlushAsync();
    Check(saved[^1].Id == "podcast:a" && saved[^1].Position == 90 && !saved[^1].Ended, "flush previous");
    tracker.Observe("podcast:b", "paused", 55);
    tracker.Observe("podcast:b", "ended", 56);
    await tracker.DisposeAsync();
    Check(saved[^1].Id == "podcast:b" && saved[^1].Ended && saved[^1].Position == 56, "host ended");

    var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var slow = new List<PodcastProgressSample>();
    var coalesced = new PodcastProgressTracker(sample => { slow.Add(sample); return slow.Count == 1 ? gate.Task : Task.CompletedTask; }, () => 0);
    coalesced.Observe("podcast:a", "playing", 1);
    await Task.Yield();
    for (var index = 2; index <= 100; index++) coalesced.Observe("podcast:a", "playing", index);
    var finish = coalesced.DisposeAsync();
    gate.SetResult();
    await finish;
    Check(slow.Count == 2 && slow[^1].Position == 100, "coalesce");

    var paused = new List<PodcastProgressSample>();
    var pauseTracker = new PodcastProgressTracker(sample => { paused.Add(sample); return Task.CompletedTask; }, () => now);
    now = 0;
    pauseTracker.Observe("podcast:a", "paused", 40);
    await pauseTracker.FlushAsync();
    for (var index = 0; index < 100; index++) pauseTracker.Observe("podcast:a", "paused", 40);
    await pauseTracker.FlushAsync();
    Check(paused.Count == 1, "unchanged pause");
    pauseTracker.Observe("podcast:a", "paused", 15);
    await pauseTracker.DisposeAsync();
    Check(paused.Count == 2 && paused[^1].Position == 15, "paused seek");

    var attempts = 0;
    now = 0;
    var failing = new PodcastProgressTracker(_ => { attempts++; return Task.FromException(new IOException("disk")); }, () => now);
    failing.Observe("podcast:a", "paused", 40);
    now = 15_001;
    failing.Observe("podcast:a", "paused", 40);
    now = 15_501;
    failing.Observe("podcast:a", "paused", 40);
    Check(attempts == 2, "failed pause throttle");
    await failing.DisposeAsync();
}

static string Stamp(int days) => DateTimeOffset.FromUnixTimeMilliseconds(days * 86_400_000L).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Expect(string code, Action action)
{
    try { action(); }
    catch (PodcastFailure error) when (error.Code == code) { return; }
    throw new InvalidOperationException(code);
}
