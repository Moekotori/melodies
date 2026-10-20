using System.Net;
using System.Text.Json;
using Echo.Contracts.Radio;

namespace Echo.Infrastructure.Radio;

public sealed class RadioDirectoryClient : IRadioDirectory
{
    private readonly object gate = new();
    private readonly HttpClient client;
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> discover;
    private readonly HashSet<CancellationTokenSource> active = [];
    private bool disposed;

    public RadioDirectoryClient(HttpMessageHandler? handler = null, Func<CancellationToken, Task<IReadOnlyList<string>>>? discover = null)
    {
        client = new(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, MaxConnectionsPerServer = 2, ConnectTimeout = TimeSpan.FromSeconds(4),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ECHO/RadioDirectory");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        this.discover = discover ?? (token => DnsSrvQuery.LookupAsync("_api._tcp.radio-browser.info", token));
    }

    public void Cancel() { lock (gate) foreach (var request in active) request.Cancel(); }

    public async Task<RadioDirectoryResult> SearchAsync(RadioDirectoryQuery query, CancellationToken token)
    {
        CancellationTokenSource mine;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (active.Count >= 4) return new([], false, "busy");
            mine = CancellationTokenSource.CreateLinkedTokenSource(token);
            active.Add(mine);
        }
        mine.CancelAfter(TimeSpan.FromSeconds(14));
        var normalized = RadioDirectoryParser.Normalize(query);
        try
        {
            var servers = await discover(mine.Token).ConfigureAwait(false);
            foreach (var server in servers.Where(RadioDirectoryParser.IsOfficialHost).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(_ => Random.Shared.Next()).Take(2))
            {
                if (mine.IsCancellationRequested) break;
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(mine.Token);
                attempt.CancelAfter(TimeSpan.FromSeconds(6));
                try
                {
                    using var response = await client.GetAsync(Address(server, normalized), HttpCompletionOption.ResponseHeadersRead, attempt.Token).ConfigureAwait(false);
                    if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                        or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                        || !response.IsSuccessStatusCode)
                        continue;
                    var bytes = await ReadAsync(response, attempt.Token).ConfigureAwait(false);
                    return RadioDirectoryParser.Parse(bytes, normalized.Offset);
                }
                catch (OperationCanceledException) when (!mine.IsCancellationRequested) { }
                catch (Exception error) when (error is HttpRequestException or IOException or JsonException) { }
            }
            return new([], false, token.IsCancellationRequested ? "cancelled" : "unavailable");
        }
        catch (OperationCanceledException) { return new([], false, token.IsCancellationRequested ? "cancelled" : "unavailable"); }
        catch (Exception error) when (error is HttpRequestException or IOException) { return new([], false, "unavailable"); }
        finally
        {
            lock (gate) active.Remove(mine);
            mine.Dispose();
        }
    }

    private static string Address(string server, RadioDirectoryQuery query)
    {
        var parts = new List<string>
        {
            "offset=" + query.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "limit=" + (RadioDirectoryLimits.PageSize + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "hidebroken=true", "order=clickcount", "reverse=true"
        };
        Add(parts, "name", query.Name);
        Add(parts, "countrycode", query.CountryCode);
        Add(parts, "language", query.Language);
        Add(parts, "tag", query.Tag);
        return "https://" + server + "/json/stations/search?" + string.Join('&', parts);
    }

    private static void Add(List<string> parts, string key, string value)
    {
        if (value.Length > 0) parts.Add(key + "=" + Uri.EscapeDataString(value));
    }

    private static async Task<byte[]> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        const int max = 256 * 1024;
        if (response.Content.Headers.ContentLength > max) throw new IOException("电台目录响应过大。");
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[max + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
        }
        if (length > max) throw new IOException("电台目录响应过大。");
        return buffer[..length];
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            foreach (var request in active) request.Cancel();
        }
        client.Dispose();
    }
}
