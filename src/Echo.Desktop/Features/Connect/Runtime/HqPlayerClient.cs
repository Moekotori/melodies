using System.Net.Sockets;
using System.Text;
using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Features.Connect.Runtime;

public sealed class HqPlayerClient : IDisposable
{
    private readonly Func<PlayerViewModel?> player;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CastStreamServer? server;

    public HqPlayerClient(Func<PlayerViewModel?> player) => this.player = player;

    public string Target { get; private set; } = "";

    public async Task ProbeAsync(string host, int port, CancellationToken token)
    {
        var info = await ExchangeAsync(host, port, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<GetInfo/>", token).ConfigureAwait(false);
        RequireXml(info, "HQPlayer 没有返回控制协议。");
        var status = await ExchangeAsync(host, port, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Status subscribe=\"0\"/>", token).ConfigureAwait(false);
        RequireXml(status, "HQPlayer 没有返回播放状态。");
    }

    public async Task HandOffAsync(string host, int port, CancellationToken token)
    {
        var track = ConnectTrack.Current(player());
        if (track is null) throw new InvalidOperationException("先播放一首本地歌曲，或一个不带账号的网络音频。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        CastStreamServer? opened = null;
        try
        {
            var value = track.Value.Location;
            if (track.Value.LocalFile && !ConnectAddress.IsLoopback(host))
            {
                opened = CastStreamServer.Start(track.Value.Location, track.Value.Mime);
                value = opened.Url(host);
            }
            var duration = player()?.Session.Snapshot.DurationSeconds ?? 0;
            var length = double.IsFinite(duration) && duration > 0 ? ((int)duration).ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            await ExchangeAsync(host, port, PlayNext(value, track.Value.Title, track.Value.Artist, track.Value.Album, length, track.Value.Mime), token).ConfigureAwait(false);
            await ExchangeAsync(host, port, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Play/>", token).ConfigureAwait(false);
            server?.Dispose();
            server = opened;
            opened = null;
            Target = host + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await ConnectPlayback.PauseLocalAsync(player(), token).ConfigureAwait(false);
        }
        finally
        {
            opened?.Dispose();
            gate.Release();
        }
    }

    public Task PauseAsync(string host, int port, CancellationToken token)
        => ExchangeAsync(host, port, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Pause/>", token);

    public async Task StopAsync(string host, int port, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await ExchangeAsync(host, port, "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<Stop/>", token).ConfigureAwait(false);
            server?.Dispose();
            server = null;
            Target = "";
        }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        server?.Dispose();
        server = null;
    }

    private static async Task<string> ExchangeAsync(string host, int port, string xml, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var bytes = Encoding.UTF8.GetBytes(xml);
            await stream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
            return await ReadXmlAsync(stream, timeout.Token).ConfigureAwait(false);
        }
        catch (SocketException error) when (error.SocketErrorCode == SocketError.ConnectionRefused)
        {
            throw new InvalidOperationException("HQPlayer 拒绝了连接。先打开 HQPlayer，并确认控制端口。");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("HQPlayer 没有在 3 秒内返回控制协议。");
        }
    }

    private static async Task<string> ReadXmlAsync(Stream stream, CancellationToken token)
    {
        var buffer = new byte[4096];
        var text = new StringBuilder();
        while (text.Length < 65536)
        {
            using var slice = CancellationTokenSource.CreateLinkedTokenSource(token);
            slice.CancelAfter(text.Length == 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromMilliseconds(300));
            int read;
            try { read = await stream.ReadAsync(buffer, slice.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (text.Length > 0) { break; }
            if (read == 0) break;
            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
            if (text.ToString().Contains("/>", StringComparison.Ordinal) || text.ToString().Contains("</", StringComparison.Ordinal)) break;
        }
        var result = text.ToString();
        RequireXml(result, "HQPlayer 没有返回可识别的控制结果。");
        return result;
    }

    private static void RequireXml(string xml, string failure)
    {
        if (!xml.Contains('<')) throw new InvalidOperationException(failure);
        if (xml.Contains("<error", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("HQPlayer 返回了错误，曲目没有被接受。");
    }

    private static string PlayNext(string value, string title, string artist, string album, string length, string mime)
    {
        var metadata = new StringBuilder();
        Append(metadata, "song", title);
        Append(metadata, "artist", artist);
        Append(metadata, "album", album);
        Append(metadata, "length", length);
        Append(metadata, "mime_type", mime);
        var command = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<PlayNextURI value=\"" + ConnectXml.Escape(value) + "\" freewheel=\"0\"";
        return metadata.Length == 0 ? command + "/>\n" : command + "><metadata " + metadata + "/></PlayNextURI>\n";
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        if (value.Length == 0) return;
        if (builder.Length > 0) builder.Append(' ');
        builder.Append(name).Append("=\"").Append(ConnectXml.Escape(value)).Append('"');
    }
}
