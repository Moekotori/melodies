using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Echo.Wallpapers;

// Eight active streams, no request body, bounded headers, loopback and a per-session resource capability.
internal sealed partial class WallpaperAssetServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _root;
    private readonly string? _assets;
    private readonly WallpaperProject _project;
    private readonly ScenePackage? _package;
    private readonly string _properties;
    private readonly string _prefix = "/" + Guid.NewGuid().ToString("N") + "/";
    private readonly Task _loop;
    public string Url { get; }
    public WallpaperAssetServer(string manifest, string? assets)
    {
        _root = Path.GetDirectoryName(Path.GetFullPath(manifest))!; _assets = assets;
        _project = WallpaperProject.Read(manifest); _properties = WallpaperPages.Properties(_root);
        if (_project.Kind == "scene" && WallpaperPaths.Inside(_root, "scene.pkg") is { } package) _package = new(package);
        _listener.Start(16);
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{_prefix}host.html";
        _loop = Run(_stop.Token);
    }
    private async Task Run(CancellationToken token)
    {
        var active = new List<Task>(8);
        try
        {
            while (!token.IsCancellationRequested)
            {
                active.RemoveAll(t => t.IsCompleted);
                if (active.Count == 8) { await Task.WhenAny(active).ConfigureAwait(false); continue; }
                var client = await _listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                active.Add(Handle(client, token));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        finally { await Task.WhenAll(active).ConfigureAwait(false); }
    }
    private async Task Handle(TcpClient client, CancellationToken cancellation)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var token = timeout.Token;
            try
            {
                var stream = client.GetStream();
                var buffer = new byte[8192]; var used = 0; var end = -1;
                while (used < buffer.Length && end < 0)
                {
                    var count = await stream.ReadAsync(buffer.AsMemory(used), token).ConfigureAwait(false);
                    if (count == 0) return;
                    used += count;
                    end = buffer.AsSpan(0, used).IndexOf("\r\n\r\n"u8);
                }
                if (end < 0) return;
                var lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
                var request = lines[0].Split(' ');
                if (request.Length != 3 || request[0] is not ("GET" or "HEAD")) { await Empty(stream, 405, token); return; }
                if (!Uri.TryCreate("http://localhost" + request[1], UriKind.Absolute, out var uri)) { await Empty(stream, 400, token); return; }
                var path = Uri.UnescapeDataString(uri.AbsolutePath);
                if (!path.StartsWith(_prefix, StringComparison.Ordinal)) { await Empty(stream, 404, token); return; }
                var name = path[_prefix.Length..];
                var range = lines.FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))?[6..].Trim();
                await Serve(stream, name, range, request[0] == "HEAD", token).ConfigureAwait(false);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or SocketException
                or OperationCanceledException or ArgumentException or System.Text.Json.JsonException) { }
        }
    }
    private Task Empty(Stream stream, int status, CancellationToken token)
        => Header(stream, status, "text/plain", 0, null, token);
    private async Task Header(Stream stream, int status, string mime, long size, string? range, CancellationToken token)
    {
        var root = Url[..^"host.html".Length];
        var csp = $"default-src 'none'; script-src {root} 'unsafe-inline' 'unsafe-eval'; style-src {root} 'unsafe-inline'; img-src {root} data: blob:; media-src {root} blob:; font-src {root} data: blob:; connect-src {root}; frame-src {root}; worker-src 'none'; object-src 'none'; form-action 'none'; base-uri 'none'";
        var header = $"HTTP/1.1 {status} OK\r\nContent-Type: {mime}\r\nContent-Length: {size}\r\nConnection: close\r\nCache-Control: no-store\r\nAccept-Ranges: bytes\r\nAccess-Control-Allow-Origin: *\r\nContent-Security-Policy: {csp}\r\nReferrer-Policy: no-referrer\r\nX-Content-Type-Options: nosniff\r\nPermissions-Policy: camera=(), microphone=(), geolocation=(), usb=(), serial=()\r\n";
        if (range is not null) header += "Content-Range: " + range + "\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header + "\r\n"), token).ConfigureAwait(false);
    }
    private async Task Text(Stream stream, string text, string mime, bool head, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await Header(stream, 200, mime, bytes.Length, null, token).ConfigureAwait(false);
        if (!head) await stream.WriteAsync(bytes, token).ConfigureAwait(false);
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop();
        await _loop.ConfigureAwait(false); _stop.Dispose();
    }
}
