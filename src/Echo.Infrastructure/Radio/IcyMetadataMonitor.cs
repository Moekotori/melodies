using System.Buffers;
using System.Globalization;
using System.Net;
using Echo.Contracts.Radio;

namespace Echo.Infrastructure.Radio;

public sealed class IcyMetadataMonitor : IRadioMetadataMonitor
{
    private static readonly int[] RetryDelays = [1_000, 3_000, 8_000, 15_000];
    private readonly object gate = new();
    private readonly HttpClient client;
    private CancellationTokenSource? connect;
    private int epoch;
    private bool disposed;
    private string? url;
    private string? title;

    public IcyMetadataMonitor(HttpMessageHandler? handler = null)
    {
        client = new(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false, MaxConnectionsPerServer = 1, ConnectTimeout = TimeSpan.FromSeconds(4),
            MaxResponseDrainSize = 0, ResponseDrainTimeout = TimeSpan.Zero, PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        }) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ECHO/Radio");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Icy-MetaData", "1");
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
    }

    public event Action? Changed;
    public string? StreamUrl { get { lock (gate) return url; } }
    public string? StreamTitle { get { lock (gate) return title; } }

    public void Follow(string? next, bool keepTitle)
    {
        var notify = false;
        lock (gate)
        {
            if (disposed) return;
            if (next is not null && next == url && connect is not null) return;
            StopLocked();
            if (next is null)
            {
                if (!keepTitle) notify = Assign(null, null);
            }
            else
            {
                notify = Assign(next, null);
                var generation = epoch;
                var request = new CancellationTokenSource();
                connect = request;
                _ = RunAsync(next, generation, request);
            }
        }
        if (notify) Notify();
    }

    private void StopLocked()
    {
        epoch++;
        connect?.Cancel();
        connect?.Dispose();
        connect = null;
    }

    private async Task RunAsync(string target, int generation, CancellationTokenSource request)
    {
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (!Current(generation)) return;
            var unsupported = false;
            try { unsupported = await ReadOnceAsync(target, request.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { return; }
            catch (Exception) when (Current(generation)) { }
            if (unsupported || !Current(generation) || attempt == RetryDelays.Length) return;
            try { await Task.Delay(RetryDelays[attempt], request.Token).ConfigureAwait(false); }
            catch (Exception error) when (error is OperationCanceledException or ObjectDisposedException) { return; }
        }
    }

    private async Task<bool> ReadOnceAsync(string target, CancellationToken token)
    {
        using var response = await OpenAsync(target, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength == 0) return false;
        if (!response.Headers.TryGetValues("icy-metaint", out var values)
            || !int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var metaint)
            || metaint is < 1 or > 64 * 1024)
            return true;
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            var filled = 0;
            var offset = 0;
            var audioRemaining = metaint;
            byte[]? meta = null;
            var metaFilled = 0;
            string? last = null;
            while (CurrentToken(token))
            {
                if (offset >= filled)
                {
                    offset = 0;
                    filled = await stream.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                    if (filled == 0) return false;
                }
                if (meta is not null)
                {
                    var take = Math.Min(meta.Length - metaFilled, filled - offset);
                    Buffer.BlockCopy(buffer, offset, meta, metaFilled, take);
                    metaFilled += take;
                    offset += take;
                    if (metaFilled < meta.Length) continue;
                    var parsed = IcyTitle.Read(meta);
                    if (parsed != last) { last = parsed; Publish(target, parsed); }
                    meta = null;
                    audioRemaining = metaint;
                    continue;
                }
                if (audioRemaining > 0)
                {
                    var skip = Math.Min(audioRemaining, filled - offset);
                    audioRemaining -= skip;
                    offset += skip;
                    continue;
                }
                var length = buffer[offset] * 16;
                offset++;
                if (length == 0) { audioRemaining = metaint; continue; }
                if (length > 16 * 255) return false;
                meta = new byte[length];
                metaFilled = 0;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        return false;
    }

    private async Task<HttpResponseMessage> OpenAsync(string target, CancellationToken token)
    {
        var current = RadioHttpAddress.Normalize(target);
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            var response = await client.GetAsync(current, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
            {
                if (response.RequestMessage?.RequestUri is { } final && final.UserInfo.Length > 0)
                { response.Dispose(); throw new IOException("电台元数据地址无效。"); }
                return response;
            }
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirect == 5) throw new IOException("电台元数据重定向无效。");
            current = RadioHttpAddress.Normalize(new Uri(new Uri(current), location).AbsoluteUri);
        }
        throw new IOException("电台元数据重定向次数过多。");
    }

    private bool Current(int generation) { lock (gate) return !disposed && generation == epoch; }
    private bool CurrentToken(CancellationToken token) => !token.IsCancellationRequested && !disposed;

    private void Publish(string target, string? parsed)
    {
        var notify = false;
        lock (gate)
        {
            if (!disposed && url == target) notify = Assign(target, parsed);
        }
        if (notify) Notify();
    }

    private bool Assign(string? nextUrl, string? nextTitle)
    {
        if (url == nextUrl && title == nextTitle) return false;
        url = nextUrl;
        title = nextTitle;
        return true;
    }

    private void Notify()
    {
        if (Changed is not { } handlers) return;
        foreach (Action handler in handlers.GetInvocationList()) try { handler(); } catch (Exception) { }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            StopLocked();
            Changed = null;
        }
        client.Dispose();
    }
}
