using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

// Loopback proxy so Digest credentials stay in this process. The audio host only sees 127.0.0.1.
internal sealed class RemoteHttpProxy : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly HttpClient client;
    private readonly ConcurrentDictionary<string, Target> targets = new();
    private readonly SemaphoreSlim streams = new(4, 4);
    private int disposed;
    public string Prefix { get; }
    public RemoteHttpProxy(HttpClient client)
    {
        this.client = client;
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Prefix = "http://127.0.0.1:" + port + "/remote/";
        listener.Prefixes.Add(Prefix); listener.Start();
        _ = Task.Run(Loop);
    }
    public string Register(Uri upstream, RemoteConnection source)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        var token = Guid.NewGuid().ToString("N");
        if (targets.Count >= 32)
            foreach (var expired in targets.Where(x => x.Value.Expires <= DateTimeOffset.UtcNow).Select(x => x.Key).Take(8))
                targets.TryRemove(expired, out _);
        if (targets.Count >= 32) targets.TryRemove(targets.Keys.First(), out _);
        targets[token] = new(upstream, source, DateTimeOffset.UtcNow.AddHours(4));
        return Prefix + token;
    }
    private async Task Loop()
    {
        while (Volatile.Read(ref disposed) == 0)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }
            _ = Task.Run(() => Serve(context));
        }
    }
    private async Task Serve(HttpListenerContext context)
    {
        var entered = false;
        try
        {
            var token = context.Request.Url?.AbsolutePath["/remote/".Length..] ?? "";
            if (context.Request.HttpMethod is not ("GET" or "HEAD") || !targets.TryGetValue(token, out var target) || target.Expires <= DateTimeOffset.UtcNow)
            { context.Response.StatusCode = 404; return; }
            await streams.WaitAsync().ConfigureAwait(false); entered = true;
            using var first = await Forward(context, target, null).ConfigureAwait(false);
            var challenged = WebDavDigest.TryChallenge(first, target.Upstream, target.Source, context.Request.HttpMethod, out var digest);
            using var second = challenged ? await Forward(context, target, digest).ConfigureAwait(false) : null;
            var body = second ?? first;
            context.Response.StatusCode = (int)body.StatusCode;
            CopyHeader(body, context, "Content-Type"); CopyHeader(body, context, "Content-Length");
            CopyHeader(body, context, "Content-Range"); CopyHeader(body, context, "Accept-Ranges");
            if (context.Request.HttpMethod == "GET")
            {
                await using var stream = await body.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await stream.CopyToAsync(context.Response.OutputStream).ConfigureAwait(false);
            }
        }
        catch (Exception) { try { context.Response.StatusCode = 502; } catch { } }
        finally { try { context.Response.Close(); } catch { } if (entered) streams.Release(); }
    }
    private async Task<HttpResponseMessage> Forward(HttpListenerContext context, Target target, AuthenticationHeaderValue? digest)
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.HttpMethod), target.Upstream);
        if (context.Request.Headers["Range"] is { Length: > 0 } range) request.Headers.TryAddWithoutValidation("Range", range);
        if (digest is null) WebDavDigest.ApplyBasic(request, target.Source); else request.Headers.Authorization = digest;
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
    }
    private static void CopyHeader(HttpResponseMessage message, HttpListenerContext context, string name)
    {
        if (message.Content.Headers.TryGetValues(name, out var content)) context.Response.Headers[name] = content.First();
        else if (message.Headers.TryGetValues(name, out var header)) context.Response.Headers[name] = header.First();
    }
    public void Dispose()
    { if (Interlocked.Exchange(ref disposed, 1) != 0) return; try { listener.Close(); } catch { } streams.Dispose(); }
    private sealed record Target(Uri Upstream, RemoteConnection Source, DateTimeOffset Expires);
}
