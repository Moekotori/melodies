using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Echo.Desktop.Features.Connect.Runtime;

internal sealed class DlnaReceiverSsdp : IDisposable
{
    private readonly string uuid;
    private readonly int port;
    private readonly CancellationTokenSource cancel = new();
    private UdpClient? listener;
    private int stopped;

    public DlnaReceiverSsdp(string uuid, int port)
    {
        this.uuid = uuid;
        this.port = port;
    }

    public bool Listening { get; private set; }

    public void Start()
    {
        try
        {
            listener = new UdpClient();
            listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Client.Bind(new IPEndPoint(IPAddress.Any, 1900));
            listener.JoinMulticastGroup(IPAddress.Parse("239.255.255.250"));
            Listening = true;
            _ = ListenAsync();
        }
        catch (SocketException) { Listening = false; }
        _ = AnnounceLoopAsync();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref stopped, 1) != 0) return;
        cancel.Cancel();
        try { Send(false); } catch (SocketException) { }
        listener?.Dispose();
        cancel.Dispose();
    }

    private async Task AnnounceLoopAsync()
    {
        Send(true);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(2));
        try
        {
            while (await timer.WaitForNextTickAsync(cancel.Token).ConfigureAwait(false)) Send(true);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ListenAsync()
    {
        while (!cancel.IsCancellationRequested && listener is not null)
        {
            try
            {
                var result = await listener.ReceiveAsync(cancel.Token).ConfigureAwait(false);
                var text = Encoding.ASCII.GetString(result.Buffer);
                if (!text.StartsWith("M-SEARCH", StringComparison.OrdinalIgnoreCase)) continue;
                var target = Header(text, "ST");
                if (!Interested(target)) continue;
                var address = ConnectAddress.HostFor(result.RemoteEndPoint.Address.ToString());
                var packet = Response(address, target);
                await listener.SendAsync(packet, result.RemoteEndPoint, cancel.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { break; }
        }
    }

    private void Send(bool alive)
    {
        using var udp = new UdpClient();
        var group = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        foreach (var address in Addresses())
        {
            foreach (var target in Targets())
            {
                var packet = Notify(address, target, alive);
                udp.Send(packet, packet.Length, group);
            }
        }
    }

    private byte[] Notify(string address, string target, bool alive)
        => Encoding.ASCII.GetBytes("NOTIFY * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nCACHE-CONTROL: max-age=180\r\nLOCATION: "
            + Location(address) + "\r\nNT: " + target + "\r\nNTS: ssdp:" + (alive ? "alive" : "byebye")
            + "\r\nSERVER: ECHO/1 UPnP/1.0\r\nUSN: " + Usn(target) + "\r\n\r\n");

    private byte[] Response(string address, string target)
        => Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=180\r\nEXT:\r\nLOCATION: " + Location(address)
            + "\r\nSERVER: ECHO/1 UPnP/1.0\r\nST: " + target + "\r\nUSN: " + Usn(target) + "\r\n\r\n");

    private string Location(string address) => "http://" + address + ":" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/description.xml";
    private string Usn(string target) => target.StartsWith("uuid:", StringComparison.Ordinal) ? target : "uuid:" + uuid + "::" + target;
    private IEnumerable<string> Targets() => ["upnp:rootdevice", "uuid:" + uuid, "urn:schemas-upnp-org:device:MediaRenderer:1"];

    private bool Interested(string target)
        => target is "ssdp:all" or "upnp:rootdevice" or "urn:schemas-upnp-org:device:MediaRenderer:1" || target == "uuid:" + uuid;

    private static IReadOnlyList<string> Addresses()
    {
        var addresses = ConnectAddress.LanIPv4();
        return addresses.Count == 0 ? ["127.0.0.1"] : addresses;
    }

    private static string Header(string message, string name)
    {
        foreach (var line in message.Split('\n'))
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) return line[(name.Length + 1)..].Trim().Trim('\r');
        return "";
    }
}
