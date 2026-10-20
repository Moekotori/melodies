using System.Net;
using System.Text.Json;
using System.Xml;
using Echo.Application.Lyrics;
using Echo.Contracts.Lyrics;

namespace Echo.Lyrics;

/// <summary>Core-owned, on-demand matching. One lookup, four providers, bounded responses and result cache.</summary>
public sealed class NetworkLyricsMatcher : IAsyncDisposable
{
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly LyricsHttp http;
    private readonly SemaphoreSlim lane = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly Dictionary<LyricsIdentity, Cached> cache = new();
    private int pending;
    private long cacheBytes;
    private const long CacheBudget = 2 * 1024 * 1024;
    private sealed record Cached(LyricsText Text, DateTime Expires, long Bytes);

    public NetworkLyricsMatcher(HttpClient? client = null)
    {
        ownsClient = client is null;
        this.client = client ?? new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 2, PooledConnectionLifetime = TimeSpan.FromMinutes(5), UseCookies = false,
            AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        http = new(this.client);
    }

    public async Task<LyricsText?> MatchAsync(LyricsIdentity query, CancellationToken token)
    {
        if (!LyricsIdentityPolicy.CanQuery(query)) return null;
        if (Interlocked.Increment(ref pending) > 8) { Interlocked.Decrement(ref pending); throw new IOException("网络歌词请求繁忙，请稍后重试。"); }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, stop.Token);
        try
        {
            await lane.WaitAsync(lifetime.Token).ConfigureAwait(false);
            try
            {
                if (cache.TryGetValue(query, out var saved) && saved.Expires > DateTime.UtcNow) return saved.Text;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                var netease = new NeteaseLyrics(http);
                var catalog = netease.SearchAsync(query, deadline.Token);
                var tasks = new[] { ObserveAsync(new AmllLyrics(http).ReadAsync(query, catalog, deadline.Token), lifetime.Token),
                    ObserveAsync(netease.ReadAsync(query, catalog, deadline.Token), lifetime.Token),
                    ObserveAsync(new QqMusicLyrics(http).ReadAsync(query, deadline.Token), lifetime.Token),
                    ObserveAsync(new LrclibLyrics(http).ReadAsync(query, deadline.Token), lifetime.Token) };
                var batches = await Task.WhenAll(tasks).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                LyricsText? best = null; var quality = -1;
                foreach (var text in batches.SelectMany(batch => batch.Texts))
                {
                    try
                    {
                        if (IsPlaceholder(text.Text)) continue;
                        var parsed = LyricsParser.Parse(text, lifetime.Token);
                        if (parsed.Lines.Count == 0) continue;
                        var rank = parsed.Lines.Any(line => line.Words.Count > 0) ? 30 : parsed.IsSynced ? 20 : 10;
                        if (text.Format == ".ttml" && parsed.IsSynced) rank += 3;
                        if (text.Translation is not null) rank++;
                        if (rank <= quality) continue;
                        best = text; quality = rank;
                    }
                    catch (Exception error) when (error is InvalidDataException or XmlException or ArgumentException) { }
                }
                if (best is not null) Store(query, best);
                else if (batches.All(batch => batch.Failed)) throw new LyricsLookupUnavailableException();
                return best;
            }
            finally { lane.Release(); }
        }
        finally { Interlocked.Decrement(ref pending); }
    }
    private static async Task<(IReadOnlyList<LyricsText> Texts, bool Failed)> ObserveAsync(Task<IReadOnlyList<LyricsText>> task, CancellationToken token)
    {
        try { return (await task.ConfigureAwait(false), false); }
        catch (Exception error) when (IsProviderFailure(error, token)) { return ([], true); }
    }
    internal static bool IsProviderFailure(Exception error, CancellationToken token) => !token.IsCancellationRequested
        && error is HttpRequestException or IOException or InvalidDataException or JsonException or OperationCanceledException or XmlException or ArgumentException;
    private static bool IsPlaceholder(string text)
    {
        var value = text.Trim();
        return value.Length < 128 && (value.Contains("纯音乐", StringComparison.Ordinal) || value.Contains("纯音樂", StringComparison.Ordinal)
            || value.Equals("instrumental", StringComparison.OrdinalIgnoreCase) || value.Contains("请欣赏", StringComparison.Ordinal));
    }
    private void Store(LyricsIdentity query, LyricsText text)
    {
        var size = 2L * (text.Text.Length + (text.Translation?.Text.Length ?? 0) + (text.Romanization?.Text.Length ?? 0)
            + query.Title.Length + query.Artist.Length + query.Album.Length);
        if (cache.Remove(query, out var old)) cacheBytes -= old.Bytes;
        if (size > CacheBudget) return;
        foreach (var key in cache.Where(pair => pair.Value.Expires <= DateTime.UtcNow).Select(pair => pair.Key).ToArray())
            if (cache.Remove(key, out var expired)) cacheBytes -= expired.Bytes;
        while (cache.Count >= 16 || cacheBytes + size > CacheBudget)
        {
            var key = cache.First().Key; cacheBytes -= cache[key].Bytes; cache.Remove(key);
        }
        cache.Add(query, new(text, DateTime.UtcNow.AddMinutes(10), size)); cacheBytes += size;
    }
    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync().ConfigureAwait(false);
        await lane.WaitAsync().ConfigureAwait(false);
        try { cache.Clear(); cacheBytes = 0; if (ownsClient) client.Dispose(); }
        finally { lane.Release(); }
        // Waiting callers may still unwind after cancellation; do not dispose their semaphore.
    }
}
