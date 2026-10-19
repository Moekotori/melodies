using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Echo.Contracts.Settings;

namespace Echo.Infrastructure.Broadcast;

/// <summary>Loopback-only read API and OBS page; two clients, eight waiting connections, 16 KiB request limit.</summary>
public sealed class LocalBroadcastService(Func<BroadcastSnapshot> snapshot, int port = 18888) : ILocalBroadcast, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private TcpListener? listener;
    private CancellationTokenSource? lifetime;
    private Task? serving;
    public string Address => $"http://127.0.0.1:{port}";
    public bool IsRunning => Volatile.Read(ref listener) is not null;
    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellation)
    {
        if (!await gate.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new IOException("输出服务正在切换，请稍后重试。");
        try
        {
            if (enabled == IsRunning) return;
            if (!enabled) { await StopAsync().ConfigureAwait(false); return; }
            if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            var server = new TcpListener(IPAddress.Loopback, port);
            try { server.Start(8); }
            catch (SocketException) { throw new IOException("本机输出端口被占用，请关闭占用 18888 端口的程序后重试。"); }
            var stop = new CancellationTokenSource();
            Volatile.Write(ref listener, server); lifetime = stop;
            serving = ServeAsync(server, stop.Token);
        }
        finally { gate.Release(); }
    }
    private async Task ServeAsync(TcpListener server, CancellationToken token)
    {
        var clients = Channel.CreateBounded<TcpClient>(new BoundedChannelOptions(8) { SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var workers = new[] { WorkerAsync(clients.Reader, token), WorkerAsync(clients.Reader, token) };
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await server.AcceptTcpClientAsync(token).ConfigureAwait(false);
                if (!clients.Writer.TryWrite(client)) client.Dispose();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (SocketException) when (token.IsCancellationRequested) { }
        finally
        {
            clients.Writer.TryComplete(); await Task.WhenAll(workers).ConfigureAwait(false);
            while (clients.Reader.TryRead(out var pending)) pending.Dispose();
        }
    }
    private async Task WorkerAsync(ChannelReader<TcpClient> clients, CancellationToken token)
    {
        try
        {
            await foreach (var client in clients.ReadAllAsync(token).ConfigureAwait(false))
            {
                using (client)
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(3));
                    try { await ReplyAsync(client.GetStream(), timeout.Token).ConfigureAwait(false); }
                    catch (Exception error) when (error is OperationCanceledException or IOException or SocketException) { }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
    private async Task ReplyAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[16384]; var length = 0; var complete = false;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), token).ConfigureAwait(false);
            if (read == 0) return; length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0) { complete = true; break; }
        }
        var request = Encoding.ASCII.GetString(buffer, 0, length);
        var lines = request.Split("\r\n", StringSplitOptions.None);
        var first = lines[0].Split(' ');
        var path = first.Length == 3 ? first[1] : "";
        var host = lines.FirstOrDefault(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))?[5..].Trim();
        var origin = lines.FirstOrDefault(line => line.StartsWith("Origin:", StringComparison.OrdinalIgnoreCase))?[7..].Trim();
        var valid = complete && first.Length == 3 && first[0] == "GET" && first[2] == "HTTP/1.1"
            && host == $"127.0.0.1:{port}" && (origin is null || origin == Address);
        byte[] body; var code = 200; var content = "application/json; charset=utf-8";
        if (!valid) { code = 400; body = "Invalid local request"u8.ToArray(); }
        else if (path is "/state" or "/api/state") body = JsonSerializer.SerializeToUtf8Bytes(snapshot(), BroadcastJsonContext.Default.BroadcastSnapshot);
        else if (path is "/" or "/overlay") { body = Encoding.UTF8.GetBytes(Overlay); content = "text/html; charset=utf-8"; }
        else { code = 404; body = "Not found"u8.ToArray(); }
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: {content}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\n");
        await stream.WriteAsync(header, token).ConfigureAwait(false); await stream.WriteAsync(body, token).ConfigureAwait(false);
    }
    private async Task StopAsync()
    {
        var server = Interlocked.Exchange(ref listener, null); if (server is null) return;
        lifetime!.Cancel(); server.Stop();
        try { if (serving is not null) await serving.ConfigureAwait(false); }
        finally { lifetime.Dispose(); lifetime = null; serving = null; }
    }
    public async ValueTask DisposeAsync()
    { await gate.WaitAsync().ConfigureAwait(false); try { await StopAsync().ConfigureAwait(false); } finally { gate.Release(); } }
    private const string Overlay = """
        <!doctype html><html><meta charset="utf-8"><meta name="viewport" content="width=device-width">
        <style>body{margin:0;background:transparent;color:white;font:24px system-ui}main{padding:20px 24px;max-width:640px}h1{font-size:28px;margin:0 0 6px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}p{margin:0;opacity:.7;font-size:18px}progress{margin-top:14px;width:100%;height:3px;accent-color:#b8a2ff}</style>
        <main><h1 id="title">melodies</h1><p id="artist">Waiting for playback</p><progress id="progress" max="1" value="0"></progress></main>
        <script>async function update(){try{const s=await(await fetch('/state',{cache:'no-store'})).json();document.querySelector('#title').textContent=s.title||'ECHO';document.querySelector('#artist').textContent=[s.artist,s.album].filter(Boolean).join(' · ');const p=document.querySelector('#progress');p.max=Math.max(1,s.durationSeconds);p.value=s.positionSeconds;}catch{document.querySelector('#artist').textContent='Output unavailable';}finally{setTimeout(update,1000)}}update()</script></html>
        """;
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BroadcastSnapshot))]
internal partial class BroadcastJsonContext : JsonSerializerContext;
