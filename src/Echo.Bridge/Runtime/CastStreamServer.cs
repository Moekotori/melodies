using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Echo.Bridge;

public sealed class CastStreamServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cancel = new();
    private readonly string token;
    private readonly string filePath;
    private readonly string mime;
    private int stopped;

    private CastStreamServer(string filePath, string mime)
    {
        this.filePath = filePath;
        this.mime = mime;
        token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        listener = new TcpListener(IPAddress.Any, 0);
    }

    public int Port { get; private set; }

    public static CastStreamServer Start(string filePath, string mime)
    {
        var server = new CastStreamServer(filePath, mime);
        server.listener.Start(4);
        server.Port = ((IPEndPoint)server.listener.LocalEndpoint).Port;
        _ = server.LoopAsync();
        return server;
    }

    public string Url(string remoteHost) => "http://" + ConnectAddress.HostFor(remoteHost) + ":" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/s/" + token;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        cancel.Cancel();
        listener.Stop();
        cancel.Dispose();
    }

    private async Task LoopAsync()
    {
        var gate = new SemaphoreSlim(4);
        while (!cancel.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                client = await listener.AcceptTcpClientAsync(cancel.Token).ConfigureAwait(false);
                await gate.WaitAsync(cancel.Token).ConfigureAwait(false);
                var accepted = client;
                client = null;
                _ = Task.Run(async () =>
                {
                    try { await ServeAsync(accepted).ConfigureAwait(false); }
                    finally { accepted.Dispose(); gate.Release(); }
                });
            }
            catch (OperationCanceledException) { client?.Dispose(); break; }
            catch (ObjectDisposedException) { client?.Dispose(); break; }
            catch (SocketException) { client?.Dispose(); break; }
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        try
        {
            await using var stream = client.GetStream();
            using var read = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
            read.CancelAfter(TimeSpan.FromSeconds(8));
            var request = await LanHttp.ReadAsync(stream, 0, read.Token).ConfigureAwait(false);
            if (request is null || request.Method is not ("GET" or "HEAD") || !TokenMatches(request.Path))
            {
                await LanHttp.WriteAsync(stream, 404, "text/plain; charset=utf-8", "找不到"u8.ToArray(), cancel.Token).ConfigureAwait(false);
                return;
            }
            await using var file = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var (start, end) = Range(request.Header("range"), file.Length);
            if (start < 0)
            {
                await LanHttp.WriteAsync(stream, 416, "text/plain; charset=utf-8", [], cancel.Token, ("Content-Range", "bytes */" + file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))).ConfigureAwait(false);
                return;
            }
            var count = end - start + 1;
            var status = start == 0 && end == file.Length - 1 ? 200 : 206;
            var headers = new List<(string, string)> { ("Accept-Ranges", "bytes") };
            if (status == 206) headers.Add(("Content-Range", "bytes " + start + "-" + end + "/" + file.Length));
            await stream.WriteAsync(Header(status, mime, count, headers), cancel.Token).ConfigureAwait(false);
            if (request.Method == "HEAD") return;
            file.Position = start;
            await LanHttp.CopyAsync(file, stream, count, cancel.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private bool TokenMatches(string path)
    {
        const string prefix = "/s/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length != prefix.Length + token.Length) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(path[prefix.Length..]), Encoding.ASCII.GetBytes(token));
    }

    private static (long Start, long End) Range(string header, long length)
    {
        if (length <= 0) return (-1, -1);
        if (header.Length == 0) return (0, length - 1);
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return (-1, -1);
        var span = header.AsSpan(6);
        var dash = span.IndexOf('-');
        if (dash <= 0) return (-1, -1);
        if (!long.TryParse(span[..dash], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var start)) return (-1, -1);
        var endText = span[(dash + 1)..];
        var end = endText.Length == 0 ? length - 1 : long.TryParse(endText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
        if (start < 0 || end < start || start >= length) return (-1, -1);
        return (start, Math.Min(end, length - 1));
    }

    private static byte[] Header(int status, string type, long count, List<(string, string)> headers)
    {
        var reason = status == 206 ? "Partial Content" : "OK";
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\nContent-Type: ").Append(type);
        builder.Append("\r\nContent-Length: ").Append(count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\r\nConnection: close\r\n");
        foreach (var header in headers) builder.Append(header.Item1).Append(": ").Append(header.Item2).Append("\r\n");
        builder.Append("\r\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
