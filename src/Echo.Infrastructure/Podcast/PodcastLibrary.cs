using System.Net;
using Echo.Contracts.Podcast;

namespace Echo.Infrastructure.Podcast;

public sealed class PodcastLibrary : IPodcastLibrary
{
    private readonly PodcastDatabase database;
    private readonly HttpClient client;
    private readonly object gate = new();
    private CancellationTokenSource? active;
    private string? activeFeed;
    private bool closed;

    public PodcastLibrary(string path, HttpMessageHandler? handler = null)
    {
        database = new(path);
        client = new(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, MaxConnectionsPerServer = 2, ConnectTimeout = TimeSpan.FromSeconds(4),
            AutomaticDecompression = DecompressionMethods.None, PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }, disposeHandler: handler is null)
        { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ECHO/Podcasts");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/rss+xml, application/xml, text/xml");
    }

    public Task<IReadOnlyList<PodcastFeed>> ListAsync(CancellationToken token) => Task.Run(database.List, token);
    public Task<PodcastEpisodePage> EpisodesAsync(PodcastEpisodeQuery query, CancellationToken token) => Task.Run(() => database.Episodes(query), token);
    public Task<PodcastEpisode?> EpisodeAsync(string id, CancellationToken token) => Task.Run(() => database.Episode(id), token);
    public Task MarkPlayedAsync(string id, bool played, CancellationToken token) => Task.Run(() => database.MarkPlayed(id, played), token);
    public Task SaveProgressAsync(string id, double position, bool ended, CancellationToken token) => Task.Run(() => database.Progress(id, position, ended), token);

    public Task<PodcastFeed> SubscribeAsync(string url, CancellationToken token) => FetchAsync(url, null, token);

    public async Task<PodcastFeed> RefreshAsync(string feedId, CancellationToken token)
    {
        var request = Reserve(feedId, token);
        try
        {
            var feed = database.Feed(feedId) ?? throw new PodcastFailure("not_found");
            return await DownloadAsync(feed.Url, feed.Id, database.Validators(feedId), request.Token, token).ConfigureAwait(false);
        }
        finally { Release(request); }
    }

    public async Task UnsubscribeAsync(string feedId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        lock (gate) if (activeFeed == feedId) active?.Cancel();
        await Task.Run(() => database.Remove(feedId), token).ConfigureAwait(false);
    }

    public void Cancel()
    {
        lock (gate) { closed = true; active?.Cancel(); }
    }

    public void Dispose()
    {
        Cancel();
        client.Dispose();
        database.Dispose();
    }

    private async Task<PodcastFeed> FetchAsync(string url, string? existingId, CancellationToken token)
    {
        var request = Reserve(null, token);
        try { return await DownloadAsync(url, existingId, default, request.Token, token).ConfigureAwait(false); }
        finally { Release(request); }
    }

    private async Task<PodcastFeed> DownloadAsync(string input, string? existingId, PodcastValidators validators, CancellationToken requestToken, CancellationToken caller)
    {
        var url = PodcastUrl.Canonical(input) ?? throw new PodcastFailure("invalid_url");
        try
        {
            var current = url;
            for (var redirect = 0; redirect <= 5; redirect++)
            {
                using var message = new HttpRequestMessage(HttpMethod.Get, current);
                if (existingId is not null)
                {
                    if (validators.Etag is not null) message.Headers.TryAddWithoutValidation("If-None-Match", validators.Etag);
                    if (validators.LastModified is not null) message.Headers.TryAddWithoutValidation("If-Modified-Since", validators.LastModified);
                }
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var location = response.Headers.Location;
                    if (location is null || redirect == 5) throw new PodcastFailure("fetch_failed");
                    current = PodcastUrl.Canonical(new Uri(new Uri(current), location).AbsoluteUri) ?? throw new PodcastFailure("fetch_failed");
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.NotModified && existingId is not null)
                    return database.NotModified(existingId, PodcastValidators.Read(Header(response, "etag"), Header(response, "last-modified")));
                if (!response.IsSuccessStatusCode) throw new PodcastFailure("fetch_failed");
                var bytes = await ReadBodyAsync(response, requestToken).ConfigureAwait(false);
                var xml = PodcastFeedDecoder.Decode(bytes, response.Content.Headers.ContentType?.ToString());
                var document = response.RequestMessage?.RequestUri?.AbsoluteUri ?? current;
                return database.Import(xml, url, document, existingId, PodcastValidators.Read(Header(response, "etag"), Header(response, "last-modified")));
            }
            throw new PodcastFailure("fetch_failed");
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) { throw new PodcastFailure("fetch_failed"); }
        catch (PodcastFailure) { throw; }
        catch (Exception) { throw new PodcastFailure("fetch_failed"); }
    }

    private static async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > PodcastLimits.FeedBytes) throw new PodcastFailure("feed_too_large");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[PodcastLimits.FeedBytes + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length), token).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
        }
        if (length > PodcastLimits.FeedBytes) throw new PodcastFailure("feed_too_large");
        return bytes[..length];
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values) ? values.FirstOrDefault() : null;

    private CancellationTokenSource Reserve(string? feedId, CancellationToken token)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (active is not null) throw new PodcastFailure("busy");
            var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            request.CancelAfter(TimeSpan.FromSeconds(12));
            active = request; activeFeed = feedId;
            return request;
        }
    }

    private void Release(CancellationTokenSource request)
    {
        lock (gate) if (active == request) { active = null; activeFeed = null; }
        request.Dispose();
    }
}
