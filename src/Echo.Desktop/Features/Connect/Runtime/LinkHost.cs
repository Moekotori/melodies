using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Features.Connect.Runtime;

public sealed class LinkHost : IDisposable
{
    private readonly Func<PlayerViewModel?> player;
    private readonly SemaphoreSlim gate = new(1, 1);
    private TcpListener? listener;
    private CancellationTokenSource? cancel;
    private string code = "";
    private DateTime codeUntil;
    private int misses;
    private DateTime lockedUntil;
    private int generation;

    public LinkHost(Func<PlayerViewModel?> player) => this.player = player;

    public bool Running { get; private set; }
    public int Port { get; private set; }
    public string Code { get; private set; } = "";
    public string Token { get; private set; } = "";
    public string Addresses { get; private set; } = "";
    public string Error { get; private set; } = "";
    public event Action? Changed;

    public string PageUrl
    {
        get
        {
            var host = ConnectAddress.LanIPv4().FirstOrDefault() ?? "127.0.0.1";
            return Running ? "http://" + host + ":" + Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/echo-link/web" : "";
        }
    }

    public async Task StartAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Running) return;
            listener = Bind(out var port);
            Port = port;
            Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            RotateCode();
            var addresses = ConnectAddress.LanIPv4();
            Addresses = addresses.Count == 0 ? "127.0.0.1" : string.Join("、", addresses);
            cancel = new CancellationTokenSource();
            Error = "";
            Running = true;
            var gen = ++generation;
            Changed?.Invoke();
            _ = AcceptAsync(listener, gen, cancel.Token);
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { StopCore(); }
        finally { gate.Release(); }
    }

    public void Rotate()
    {
        if (!Running) return;
        RotateCode();
        Changed?.Invoke();
    }

    public void Dispose() => StopCore();

    private async Task AcceptAsync(TcpListener bound, int gen, CancellationToken token)
    {
        var limit = new SemaphoreSlim(4);
        while (!token.IsCancellationRequested && gen == generation)
        {
            TcpClient? client = null;
            try
            {
                client = await bound.AcceptTcpClientAsync(token).ConfigureAwait(false);
                await limit.WaitAsync(token).ConfigureAwait(false);
                var accepted = client;
                client = null;
                _ = Task.Run(async () =>
                {
                    try { await HandleAsync(accepted, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception) { }
                    finally { accepted.Dispose(); limit.Release(); }
                });
            }
            catch (OperationCanceledException) { client?.Dispose(); break; }
            catch (ObjectDisposedException) { client?.Dispose(); break; }
            catch (SocketException) { client?.Dispose(); break; }
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        await using var stream = client.GetStream();
        using var read = CancellationTokenSource.CreateLinkedTokenSource(token);
        read.CancelAfter(TimeSpan.FromSeconds(8));
        var request = await LanHttp.ReadAsync(stream, 8192, read.Token).ConfigureAwait(false);
        if (request is null) return;
        if (request.Method == "POST" && request.Path == "/echo-link/pair")
        {
            await PairAsync(stream, request, token).ConfigureAwait(false);
            return;
        }
        if (request.Method == "POST" && request.Path == "/echo-link/command")
        {
            await CommandAsync(stream, request, token).ConfigureAwait(false);
            return;
        }
        if (request.Method == "GET" && request.Path is "/echo-link/web" or "/echo-link" or "/")
        {
            var form = request.Form();
            if (Authorized(form.GetValueOrDefault("token") ?? "")) await RemoteAsync(stream, token).ConfigureAwait(false);
            else await LanHttp.WriteAsync(stream, 200, "text/html; charset=utf-8", LinkPages.Pair(PairHint()), token).ConfigureAwait(false);
            return;
        }
        await LanHttp.WriteAsync(stream, 404, "text/plain; charset=utf-8", "找不到"u8.ToArray(), token).ConfigureAwait(false);
    }

    private async Task PairAsync(Stream stream, LanRequest request, CancellationToken token)
    {
        var entered = request.Form().GetValueOrDefault("code") ?? "";
        if (DateTime.UtcNow < lockedUntil)
        {
            await LanHttp.WriteAsync(stream, 200, "text/html; charset=utf-8", LinkPages.Pair("试得太频繁，请稍后再试。"), token).ConfigureAwait(false);
            return;
        }
        if (DateTime.UtcNow > codeUntil || !Fixed(entered, code))
        {
            misses++;
            if (misses >= 5) { lockedUntil = DateTime.UtcNow.AddSeconds(60); RotateCode(); }
            Changed?.Invoke();
            await LanHttp.WriteAsync(stream, 200, "text/html; charset=utf-8", LinkPages.Pair("配对码不正确或已过期。"), token).ConfigureAwait(false);
            return;
        }
        misses = 0;
        var location = "/echo-link/web?token=" + Token;
        await LanHttp.WriteAsync(stream, 303, "text/plain", [], token, ("Location", location)).ConfigureAwait(false);
    }

    private async Task CommandAsync(Stream stream, LanRequest request, CancellationToken token)
    {
        var form = request.Form();
        if (!Authorized(form.GetValueOrDefault("token") ?? ""))
        {
            await LanHttp.WriteAsync(stream, 200, "text/html; charset=utf-8", LinkPages.Pair("需要先配对。"), token).ConfigureAwait(false);
            return;
        }
        try { await ConnectPlayback.TransportAsync(player(), form.GetValueOrDefault("action") ?? "", token).ConfigureAwait(false); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        {
            Error = error.Message;
            Changed?.Invoke();
        }
        await LanHttp.WriteAsync(stream, 303, "text/plain", [], token, ("Location", "/echo-link/web?token=" + Token)).ConfigureAwait(false);
    }

    private async Task RemoteAsync(Stream stream, CancellationToken token)
    {
        var snap = player()?.Session.Snapshot;
        var track = snap?.Current;
        var title = track?.Metadata?.GetValueOrDefault("title") ?? (track is null ? "暂无播放" : Path.GetFileNameWithoutExtension(track.FilePath));
        var artist = track?.Metadata?.GetValueOrDefault("artist") ?? "";
        var page = LinkPages.Remote(Token, title, artist, snap?.Status.ToString() ?? "Stopped", (int)Math.Round((snap?.Volume ?? 0) * 100));
        await LanHttp.WriteAsync(stream, 200, "text/html; charset=utf-8", page, token).ConfigureAwait(false);
    }

    private void RotateCode()
    {
        code = RandomNumberGenerator.GetInt32(0, 1000000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
        Code = code;
        codeUntil = DateTime.UtcNow.AddMinutes(3);
        misses = 0;
    }

    private string PairHint() => DateTime.UtcNow < lockedUntil ? "配对暂时锁定。" : "配对码显示在电脑的连接页上。";

    private bool Authorized(string supplied)
        => supplied.Length == Token.Length && Token.Length > 0
           && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(supplied), System.Text.Encoding.ASCII.GetBytes(Token));

    private static bool Fixed(string supplied, string expected)
        => supplied.Length == expected.Length && expected.Length > 0
           && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(supplied), System.Text.Encoding.ASCII.GetBytes(expected));

    private void StopCore()
    {
        generation++;
        Running = false;
        Code = "";
        Token = "";
        code = "";
        listener?.Stop();
        listener = null;
        cancel?.Cancel();
        cancel?.Dispose();
        cancel = null;
        Error = "";
        Changed?.Invoke();
    }

    private static TcpListener Bind(out int port)
    {
        foreach (var candidate in new[] { 26789, 0 })
        {
            var bound = new TcpListener(IPAddress.Any, candidate);
            try
            {
                bound.Start(8);
                port = ((IPEndPoint)bound.LocalEndpoint).Port;
                return bound;
            }
            catch (SocketException) when (candidate != 0) { bound.Stop(); }
        }
        throw new IOException("没有可用端口来打开 ECHO Link。");
    }
}
