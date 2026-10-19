using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Echo.Playback;
using Echo.Contracts.Settings;

namespace Echo.Bridge;

public sealed class DlnaReceiverHost : IDisposable
{
    private readonly Func<IPlaybackSession?> player;
    private readonly SemaphoreSlim gate = new(1, 1);
    private TcpListener? listener;
    private CancellationTokenSource? cancel;
    private DlnaReceiverSsdp? ssdp;
    private string uuid = "";
    private string mediaUri = "";
    private int generation;

    public DlnaReceiverHost(Func<IPlaybackSession?> player) => this.player = player;

    public bool Running { get; private set; }
    public int Port { get; private set; }
    public string Addresses { get; private set; } = "";
    public string Source { get; private set; } = "未连接";
    public string Error { get; private set; } = "";
    public bool Advertising { get; private set; }
    public string Name { get; private set; } = "";
    public event Action? Changed;

    public async Task StartAsync(IBridgePreferences preferences)
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Running) return;
            Name = "ECHO " + ConnectAddress.Machine();
            uuid = DlnaReceiverPages.Uuid(preferences);
            listener = Bind(out var port);
            Port = port;
            var addresses = ConnectAddress.LanIPv4();
            Addresses = addresses.Count == 0 ? "127.0.0.1" : string.Join("、", addresses);
            cancel = new CancellationTokenSource();
            var gen = ++generation;
            ssdp = new DlnaReceiverSsdp(uuid, Port);
            ssdp.Start();
            Advertising = ssdp.Listening;
            Error = Advertising ? "" : "SSDP 端口被占用，手机可能搜不到这台电脑。描述地址仍然可用。";
            Running = true;
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

    public async Task StopPlaybackAsync(CancellationToken token = default)
    {
        mediaUri = "";
        Source = "未连接";
        Changed?.Invoke();
        await ConnectPlayback.TransportAsync(player(), "stop", token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        generation++;
        listener?.Stop();
        ssdp?.Dispose();
        cancel?.Cancel();
        cancel?.Dispose();
        Running = false;
    }

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
        var request = await LanHttp.ReadAsync(stream, 512 * 1024, read.Token).ConfigureAwait(false);
        if (request is null) return;
        if (request.Method == "SUBSCRIBE")
        {
            await LanHttp.WriteAsync(stream, 200, "text/plain", [], token, ("TIMEOUT", "Second-180"), ("SID", "uuid:" + uuid)).ConfigureAwait(false);
            return;
        }
        if (request.Method == "GET" && request.Path == "/description.xml")
        {
            await LanHttp.WriteAsync(stream, 200, "text/xml; charset=utf-8", DlnaReceiverPages.Description(Name, uuid), token).ConfigureAwait(false);
            return;
        }
        if (request.Method == "GET" && request.Path is "/upnp/av.xml" or "/upnp/rc.xml")
        {
            await LanHttp.WriteAsync(stream, 200, "text/xml; charset=utf-8", DlnaReceiverPages.Scpd(request.Path.Contains("/av.", StringComparison.Ordinal) ? "av" : "rc"), token).ConfigureAwait(false);
            return;
        }
        if (request.Method == "POST" && request.Path is "/upnp/av" or "/upnp/rc")
        {
            var (status, body) = await SoapAsync(request, token).ConfigureAwait(false);
            await LanHttp.WriteAsync(stream, status, "text/xml; charset=utf-8", body, token).ConfigureAwait(false);
            return;
        }
        await LanHttp.WriteAsync(stream, 404, "text/plain; charset=utf-8", "找不到"u8.ToArray(), token).ConfigureAwait(false);
    }

    private async Task<(int Status, byte[] Body)> SoapAsync(LanRequest request, CancellationToken token)
    {
        var action = DlnaReceiverPages.ActionName(request);
        var xml = Encoding.UTF8.GetString(request.Body);
        var service = request.Path == "/upnp/rc" ? "urn:schemas-upnp-org:service:RenderingControl:1" : "urn:schemas-upnp-org:service:AVTransport:1";
        try
        {
            var body = await ActionAsync(action, xml, token).ConfigureAwait(false);
            return (200, DlnaReceiverPages.Response(service, action.Length == 0 ? "Action" : action, body));
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or HttpRequestException)
        {
            return (500, DlnaReceiverPages.Fault(error.Message));
        }
    }

    private async Task<string> ActionAsync(string action, string xml, CancellationToken token)
    {
        switch (action)
        {
            case "SetAVTransportURI":
                var uri = ConnectXml.Argument(xml, "CurrentURI");
                if (!ConnectAddress.IsHttp(uri)) throw new ArgumentException("只接受不带账号的 http 或 https 音频地址。");
                mediaUri = uri;
                Source = uri.Length > 80 ? uri[..80] : uri;
                Changed?.Invoke();
                return "";
            case "Play":
                if (mediaUri.Length == 0) throw new InvalidOperationException("还没有收到要播放的地址。");
                await ConnectPlayback.PlayHttpAsync(player(), mediaUri, null, token).ConfigureAwait(false);
                Source = "正在播放";
                Changed?.Invoke();
                return "";
            case "Pause":
                await ConnectPlayback.TransportAsync(player(), "pause", token).ConfigureAwait(false);
                return "";
            case "Stop":
                mediaUri = "";
                Source = "已停止";
                Changed?.Invoke();
                await ConnectPlayback.TransportAsync(player(), "stop", token).ConfigureAwait(false);
                return "";
            case "GetTransportInfo":
                return "<CurrentTransportState>" + ConnectPlayback.Transport(player()) + "</CurrentTransportState><CurrentTransportStatus>OK</CurrentTransportStatus>";
            case "GetPositionInfo":
                var snap = player()?.Snapshot;
                return "<TrackURI>" + ConnectXml.Escape(mediaUri) + "</TrackURI><RelTime>" + ConnectPlayback.Clock(snap?.PositionSeconds ?? 0)
                    + "</RelTime><TrackDuration>" + ConnectPlayback.Clock(snap?.DurationSeconds ?? 0) + "</TrackDuration>";
            case "SetVolume":
                if (!int.TryParse(ConnectXml.Argument(xml, "DesiredVolume"), out var volume)) throw new ArgumentException("音量无效。");
                await ConnectPlayback.TransportAsync(player(), "volume:" + Math.Clamp(volume, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture), token).ConfigureAwait(false);
                return "";
            case "GetVolume":
                var current = (int)Math.Round(Math.Clamp(player()?.Snapshot.Volume ?? 0, 0, 1) * 100);
                return "<CurrentVolume>" + current.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</CurrentVolume>";
            default:
                throw new ArgumentException("不支持这个接收命令。");
        }
    }

    private void StopCore()
    {
        generation++;
        Running = false;
        Advertising = false;
        listener?.Stop();
        listener = null;
        ssdp?.Dispose();
        ssdp = null;
        cancel?.Cancel();
        cancel?.Dispose();
        cancel = null;
        Error = "";
        Source = "未连接";
        Changed?.Invoke();
    }

    private static TcpListener Bind(out int port)
    {
        foreach (var candidate in new[] { 41952, 0 })
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
        throw new IOException("没有可用端口来监听 DLNA。");
    }
}
