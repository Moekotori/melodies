using System.Diagnostics;
using Echo.Application.Lyrics;
using Echo.Contracts.Lyrics;
using Echo.Core;
using Echo.Core.Lyrics;
using Echo.Lyrics;

if (args.Contains("--live"))
{
    await using var live = new NetworkLyricsMatcher();
    var timer = Stopwatch.StartNew();
    var text = await live.MatchAsync(new("晴天", "周杰伦", "叶惠美", 269), CancellationToken.None);
    if (text is null) throw new Exception("No live lyrics matched.");
    var document = LyricsParser.Parse(text);
    Console.WriteLine($"LIVE {text.Origin}; lines={document.Lines.Count}; words={document.Lines.Sum(line => line.Words.Count)}; elapsedMs={timer.ElapsedMilliseconds}");
    return;
}
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(25));
var token = budget.Token;
var identity = Fixtures.Identity;
Check(!LyricsIdentityPolicy.Accept(identity, identity with { Artist = "Another singer" }), "Wrong singer accepted");
Check(!LyricsIdentityPolicy.Accept(identity, identity with { Title = "Different song" }), "Wrong title accepted");
Check(!LyricsIdentityPolicy.Accept(identity, identity with { Title = identity.Title + " (Live)" }), "Live version accepted");
Check(!LyricsIdentityPolicy.Accept(identity, identity with { DurationSeconds = 600 }), "Wrong duration accepted");
Check(!LyricsIdentityPolicy.Accept(identity, identity with { DurationSeconds = 0 }), "Missing duration invented");
Check(LyricsIdentityPolicy.Accept(identity, identity with { Title = "Ｓｏｎｇ", Artist = "Singer / Guest" }), "NFKC or artist credits failed");

var requests = new List<string>();
using var fixtureClient = new HttpClient(new FixtureHttp((request, _) =>
{
    requests.Add(request.RequestUri!.Host + request.RequestUri.AbsolutePath);
    return Task.FromResult(Fixtures.Response(request));
}));
await using (var matcher = new NetworkLyricsMatcher(fixtureClient))
{
    var result = await matcher.MatchAsync(identity, token);
    Check(result?.Origin.StartsWith("AMLL", StringComparison.Ordinal) == true, "TTML did not outrank line lyrics");
    var parsed = LyricsParser.Parse(result!);
    Check(parsed.IsSynced && parsed.Lines[0].Words.Count == 2, "AMLL words lost");
    Check(requests.Any(url => url.StartsWith("music.163.com", StringComparison.Ordinal)) && requests.Any(url => url.StartsWith("u.y.qq.com", StringComparison.Ordinal))
        && requests.Any(url => url.StartsWith("lrclib.net", StringComparison.Ordinal)), "Provider missing");
    var count = requests.Count;
    Check(await matcher.MatchAsync(identity, token) == result && requests.Count == count, "Result cache issued more HTTP");
    Console.WriteLine("PASS four providers, duration evidence, TTML words and bounded cache reuse");
}

// Exercise each real provider mapper, rather than letting the preferred provider hide a broken fallback.
var http = new LyricsHttp(fixtureClient);
var netease = new NeteaseLyrics(http);
var catalog = netease.SearchAsync(identity, token);
var neteaseText = (await netease.ReadAsync(identity, catalog, token)).Single();
Check(LyricsParser.Parse(neteaseText).Lines[0].Words.Count == 2 && neteaseText.Translation is not null, "NetEase YRC/translation mapping failed");
var qqText = (await new QqMusicLyrics(http).ReadAsync(identity, token)).Single();
Check(qqText.Text.Contains("hello", StringComparison.Ordinal) && qqText.Translation is not null, "QQ base64 lyrics/translation failed");
Check((await new LrclibLyrics(http).ReadAsync(identity, token)).Single().Format == ".lrc", "LRCLIB mapping failed");

using var fallbackClient = new HttpClient(new FixtureHttp((request, _) => Task.FromResult(
    request.RequestUri!.Host == "api.amll.dev" ? new(System.Net.HttpStatusCode.ServiceUnavailable) : Fixtures.Response(request))));
await using (var matcher = new NetworkLyricsMatcher(fallbackClient))
    Check((await matcher.MatchAsync(identity, token))?.Origin.StartsWith("网易云", StringComparison.Ordinal) == true, "A failed provider swallowed successful results");

var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var canceledRequests = 0;
using var slowClient = new HttpClient(new FixtureHttp(async (_, cancellation) =>
{
    began.TrySetResult();
    try { await Task.Delay(Timeout.Infinite, cancellation); }
    catch (OperationCanceledException) { Interlocked.Increment(ref canceledRequests); throw; }
    throw new Exception("Unreachable");
}));
await using (var matcher = new NetworkLyricsMatcher(slowClient))
{
    using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token);
    var pending = matcher.MatchAsync(identity, cancel.Token);
    await began.Task.WaitAsync(token); cancel.Cancel();
    try { await pending; throw new Exception("Canceled match returned a result"); } catch (OperationCanceledException) { }
    Check(canceledRequests >= 4, "Cancellation left provider HTTP alive");
}
Console.WriteLine("PASS wrong-song rejection, provider fallback and HTTP cancellation");

var local = new StubLyrics();
var lookups = 0;
await using (var source = new CoreLyricsSource(local, (_, _) => { lookups++; return Task.FromResult<LyricsIdentity?>(identity); }, true, new(fixtureClient)))
{
    local.Result = new("[00:01.00]local", ".lrc", "local");
    Check((await source.ReadAsync("song.flac", null, token))?.Origin == "local" && lookups == 0, "Local priority lost");
    local.Result = null;
    Check(await source.ReadAsync("song.flac", "chosen.lrc", token) is null && lookups == 0, "Explicit import invoked network");
    source.SetEnabled(false);
    Check(await source.ReadAsync("song.flac", null, token) is null && lookups == 0, "Disabled setting still matched");
    source.SetEnabled(true);
    Check(await source.ReadAsync("song.flac", null, token) is not null && lookups == 1, "Core did not enable fallback");
}
Check(local.Disposed, "Core did not release local source");
var root = Path.GetFullPath(Path.Combine("misc", "network-lyrics-smoke", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var options = new CoreRuntimeOptions(root);
var settings = new CoreSettingsStore(options);
Check(settings.Read("network-lyrics", new()).Enabled, "Network lyrics is not enabled by default");
settings.Confirm("network-lyrics", new(false));
Check(!new CoreSettingsStore(options).Read("network-lyrics", new(true)).Enabled, "Saved off setting was overwritten by default");
Console.WriteLine("PASS Core local/import priority, enable/disable, default-on persistence and release");
await FailureChecks.RunAsync(token);
await CoreBoundarySmoke.RunAsync(Path.Combine(root, "ipc"), token);

static void Check(bool value, string message) { if (!value) throw new Exception(message); }

sealed class StubLyrics : ILyricsSource, IAsyncDisposable
{
    public LyricsText? Result { get; set; }
    public bool Disposed { get; private set; }
    public Task<LyricsText?> ReadAsync(string audioPath, string? explicitPath, CancellationToken cancellation) => Task.FromResult(Result);
    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
