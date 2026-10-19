using Echo.Playback;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Echo.Bridge;

public sealed class MqttBridge : IDisposable
{
    private readonly Func<IPlaybackSession?> player;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SemaphoreSlim send = new(1, 1);
    private TcpClient? client;
    private Stream? stream;
    private CancellationTokenSource? cancel;
    private IPlaybackSession? attached;
    private string prefix = "";
    private int publishVersion;
    private int publishing;
    private string? publishedItem;
    private PlaybackStatus publishedStatus;
    private double publishedVolume;
    private int disposed;

    public MqttBridge(Func<IPlaybackSession?> player) => this.player = player;

    public bool Connected { get; private set; }
    public string Error { get; private set; } = "";
    public string LastCommand { get; private set; } = "";
    public event Action? Changed;

    public async Task ConnectAsync(string host, int port, string topicPrefix, string username, string password, CancellationToken token)
    {
        if (!ConnectAddress.IsHost(host) || !ConnectAddress.IsTopic(topicPrefix)) throw new ArgumentException("主机或主题前缀无效。");
        if (username.Length > 64 || password.Length > 128 || username.Any(char.IsControl) || password.Any(char.IsControl))
            throw new ArgumentException("账号或密码无效。密码只留在这次连接里，不会写入设置。");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            var tcp = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            await tcp.ConnectAsync(host, port, timeout.Token).ConfigureAwait(false);
            var link = tcp.GetStream();
            await SendAsync(link, ConnectPacket(username, password), timeout.Token).ConfigureAwait(false);
            var ack = await ReadPacketAsync(link, timeout.Token).ConfigureAwait(false);
            if (ack.Type != 2 || ack.Body.Length < 2 || ack.Body[1] != 0)
            {
                tcp.Dispose();
                throw new InvalidOperationException(Ack(ack.Body.Length < 2 ? (byte)255 : ack.Body[1]));
            }
            prefix = topicPrefix;
            await SendAsync(link, Subscribe(prefix + "/command"), timeout.Token).ConfigureAwait(false);
            client = tcp;
            stream = link;
            cancel = new CancellationTokenSource();
            Connected = true;
            Error = "";
            Attach();
            Changed?.Invoke();
            _ = PingAsync(cancel.Token);
            _ = ReadLoopAsync(cancel.Token);
            await PublishAsync(prefix + "/availability", "online", cancel.Token).ConfigureAwait(false);
            await PublishStateAsync(cancel.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        await gate.WaitAsync().ConfigureAwait(false);
        try { await DisconnectCoreAsync().ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        _ = DisconnectAsync();
    }

    private void Attach()
    {
        attached = player();
        if (attached is not null) attached.Changed += OnPlayer;
    }

    private void OnPlayer(PlaybackSnapshot snapshot)
    {
        if (snapshot.Current?.ItemId == publishedItem && snapshot.Status == publishedStatus && snapshot.Volume == publishedVolume) return;
        publishedItem = snapshot.Current?.ItemId; publishedStatus = snapshot.Status; publishedVolume = snapshot.Volume;
        if (Interlocked.Exchange(ref publishing, 1) != 0) return;
        var version = publishVersion;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(400).ConfigureAwait(false);
                if (version != publishVersion || cancel is null) return;
                await PublishStateAsync(cancel.Token).ConfigureAwait(false);
            }
            catch (Exception) { }
            finally { Volatile.Write(ref publishing, 0); }
        });
    }

    private async Task ReadLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && stream is not null)
            {
                var packet = await ReadPacketAsync(stream, token).ConfigureAwait(false);
                if (packet.Type != 3) continue;
                var (topic, payload) = PublishPayload(packet);
                if (topic == prefix + "/command") await ExecuteAsync(payload, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            Error = error is SocketException or IOException ? "和 MQTT 代理的连接中断了。" : error.Message;
            Connected = false;
            Changed?.Invoke();
        }
    }

    private async Task ExecuteAsync(string command, CancellationToken token)
    {
        var text = command.Trim().ToLowerInvariant();
        if (text.StartsWith("volume ", StringComparison.Ordinal)) text = "volume:" + text[7..].Trim();
        try
        {
            await ConnectPlayback.TransportAsync(player(), text, token).ConfigureAwait(false);
            LastCommand = text + " 已执行";
            Changed?.Invoke();
            await PublishAsync(prefix + "/result", text + " ok", token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException)
        {
            LastCommand = error.Message;
            Changed?.Invoke();
            await PublishAsync(prefix + "/result", error.Message, token).ConfigureAwait(false);
        }
    }

    private async Task PublishStateAsync(CancellationToken token)
    {
        var current = player();
        var snap = current?.Snapshot;
        var track = snap?.Current;
        var title = track?.Metadata?.GetValueOrDefault("title") ?? (track is null ? "" : Path.GetFileNameWithoutExtension(track.FilePath));
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("title", title);
            json.WriteString("artist", track?.Metadata?.GetValueOrDefault("artist") ?? "");
            json.WriteString("status", snap?.Status.ToString() ?? "Stopped");
            json.WriteNumber("volume", (int)Math.Round((snap?.Volume ?? current?.Snapshot.Volume ?? 0) * 100));
            json.WriteEndObject();
        }
        await PublishAsync(prefix + "/state", Encoding.UTF8.GetString(buffer.ToArray()), token).ConfigureAwait(false);
    }

    private async Task PublishAsync(string topic, string payload, CancellationToken token)
        => await SendCurrentAsync(PublishPacket(topic, payload), token).ConfigureAwait(false);

    private async Task SendCurrentAsync(byte[] packet, CancellationToken token)
    {
        var link = stream;
        if (link is null) return;
        await SendAsync(link, packet, token).ConfigureAwait(false);
    }

    private async Task PingAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                await SendCurrentAsync([0xC0, 0x00], token).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private async Task SendAsync(Stream link, byte[] packet, CancellationToken token)
    {
        await send.WaitAsync(token).ConfigureAwait(false);
        try { await link.WriteAsync(packet, token).ConfigureAwait(false); }
        finally { send.Release(); }
    }

    private async Task DisconnectCoreAsync()
    {
        publishVersion++;
        if (attached is not null) attached.Changed -= OnPlayer;
        attached = null;
        if (Connected && stream is not null)
        {
            try { await PublishAsync(prefix + "/availability", "offline", CancellationToken.None).ConfigureAwait(false); }
            catch (Exception) { }
        }
        Connected = false;
        LastCommand = "";
        cancel?.Cancel();
        client?.Dispose();
        client = null;
        stream = null;
        cancel?.Dispose();
        cancel = null;
        Changed?.Invoke();
    }

    private async Task<MqttPacket> ReadPacketAsync(Stream link, CancellationToken token)
    {
        var header = new byte[1];
        if (await link.ReadAsync(header, token).ConfigureAwait(false) != 1) throw new IOException("MQTT 连接已关闭。");
        var remaining = 0;
        var multiplier = 1;
        for (var i = 0; i < 4; i++)
        {
            var digit = new byte[1];
            if (await link.ReadAsync(digit, token).ConfigureAwait(false) != 1) throw new IOException("MQTT 连接已关闭。");
            remaining += (digit[0] & 0x7F) * multiplier;
            multiplier *= 128;
            if ((digit[0] & 0x80) == 0) break;
        }
        if (remaining is < 0 or > 8192) throw new IOException("MQTT 报文过大。");
        var body = new byte[remaining];
        var read = 0;
        while (read < remaining)
        {
            var count = await link.ReadAsync(body.AsMemory(read), token).ConfigureAwait(false);
            if (count == 0) throw new IOException("MQTT 连接已关闭。");
            read += count;
        }
        return new MqttPacket(header[0] >> 4, header[0] & 0x0F, body);
    }

    private static byte[] ConnectPacket(string username, string password)
    {
        var payload = new MemoryStream();
        WriteString(payload, "MQTT");
        payload.WriteByte(4);
        var flags = (byte)0x02;
        if (username.Length > 0) flags |= 0x80;
        if (username.Length > 0 && password.Length > 0) flags |= 0x40;
        payload.WriteByte(flags);
        payload.WriteByte(0);
        payload.WriteByte(30);
        WriteString(payload, "echo-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4)).ToLowerInvariant());
        if (username.Length > 0) WriteString(payload, username);
        if (username.Length > 0 && password.Length > 0) WriteString(payload, password);
        return Frame(1, 0, payload.ToArray());
    }

    private static byte[] Subscribe(string topic)
    {
        var payload = new MemoryStream();
        payload.WriteByte(0);
        payload.WriteByte(1);
        WriteString(payload, topic);
        payload.WriteByte(0);
        return Frame(8, 2, payload.ToArray());
    }

    private static byte[] PublishPacket(string topic, string payload)
    {
        var body = new MemoryStream();
        WriteString(body, topic);
        var text = Encoding.UTF8.GetBytes(payload);
        body.Write(text);
        return Frame(3, 0, body.ToArray());
    }

    private static byte[] Frame(int type, int flags, byte[] body)
    {
        var packet = new MemoryStream();
        packet.WriteByte((byte)((type << 4) | flags));
        var length = body.Length;
        do
        {
            var digit = length % 128;
            length /= 128;
            if (length > 0) digit |= 0x80;
            packet.WriteByte((byte)digit);
        }
        while (length > 0);
        packet.Write(body);
        return packet.ToArray();
    }

    private static void WriteString(Stream stream, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        stream.WriteByte((byte)(bytes.Length >> 8));
        stream.WriteByte((byte)bytes.Length);
        stream.Write(bytes);
    }

    private static (string Topic, string Payload) PublishPayload(MqttPacket packet)
    {
        var body = packet.Body;
        if (body.Length < 2) return ("", "");
        var length = (body[0] << 8) | body[1];
        var offset = 2 + length;
        if ((packet.Flags & 0x06) != 0) offset += 2;
        if (length < 0 || offset > body.Length) return ("", "");
        return (Encoding.UTF8.GetString(body, 2, length), Encoding.UTF8.GetString(body, offset, body.Length - offset));
    }

    private static string Ack(byte code) => code switch
    {
        4 => "MQTT 账号或密码被拒绝。",
        5 => "MQTT 代理拒绝了连接。",
        _ => "MQTT 没有接受这次连接。"
    };

    private readonly record struct MqttPacket(int Type, int Flags, byte[] Body);
}
