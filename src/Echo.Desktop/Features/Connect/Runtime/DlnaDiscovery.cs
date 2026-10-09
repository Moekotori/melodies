using System.Net;
using System.Text;

namespace Echo.Desktop.Features.Connect.Runtime;

public sealed record DlnaService(string ServiceType, string ControlUrl);
public sealed record DlnaRenderer(string Name, string Model, string Address, DlnaService AvTransport, DlnaService? Rendering);

internal static class DlnaDiscovery
{
    public static async Task<IReadOnlyList<DlnaRenderer>> SearchAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var locations = await SearchLocationsAsync(timeout.Token).ConfigureAwait(false);
        var devices = new List<DlnaRenderer>();
        using var gate = new SemaphoreSlim(4);
        var work = locations.Take(12).Select(async location =>
        {
            await gate.WaitAsync(timeout.Token).ConfigureAwait(false);
            try { return await DescribeAsync(location, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
            finally { gate.Release(); }
        });
        foreach (var device in await Task.WhenAll(work).ConfigureAwait(false))
        {
            if (device is null || devices.Any(item => item.AvTransport.ControlUrl == device.AvTransport.ControlUrl)) continue;
            devices.Add(device);
        }
        return devices;
    }

    private static async Task<List<string>> SearchLocationsAsync(CancellationToken token)
    {
        using var udp = new System.Net.Sockets.UdpClient();
        var endpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
        foreach (var target in new[] { "urn:schemas-upnp-org:device:MediaRenderer:1", "urn:schemas-upnp-org:service:AVTransport:1" })
        {
            var request = Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + target + "\r\n\r\n");
            await udp.SendAsync(request, endpoint, token).ConfigureAwait(false);
        }
        var locations = new List<string>();
        using var window = CancellationTokenSource.CreateLinkedTokenSource(token);
        window.CancelAfter(TimeSpan.FromMilliseconds(2200));
        while (locations.Count < 16)
        {
            try
            {
                var result = await udp.ReceiveAsync(window.Token).ConfigureAwait(false);
                var text = Encoding.ASCII.GetString(result.Buffer);
                var location = Header(text, "LOCATION");
                if (ConnectAddress.IsHttp(location) && !locations.Contains(location)) locations.Add(location);
            }
            catch (OperationCanceledException) { break; }
        }
        return locations;
    }

    private static async Task<DlnaRenderer?> DescribeAsync(string location, CancellationToken token)
    {
        using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 2, ConnectTimeout = TimeSpan.FromSeconds(3) }) { Timeout = TimeSpan.FromSeconds(4) };
        using var response = await client.GetAsync(location, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength is > 262144) return null;
        var xml = await ReadTextAsync(response, token).ConfigureAwait(false);
        var transport = Service(xml, location, "AVTransport");
        if (transport is null) return null;
        var address = response.RequestMessage?.RequestUri?.Host ?? "";
        var name = ConnectXml.Text(xml, "friendlyName");
        if (name.Length == 0) name = address.Length == 0 ? "DLNA" : address;
        var model = ConnectXml.Text(xml, "modelName");
        return new DlnaRenderer(Trim(name), Trim(model), address, transport, Service(xml, location, "RenderingControl"));
    }

    private static DlnaService? Service(string xml, string location, string kind)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var basis)) return null;
        var root = ConnectXml.Text(xml, "URLBase");
        if (Uri.TryCreate(root, UriKind.Absolute, out var urlBase)) basis = urlBase;
        foreach (var block in ConnectXml.Blocks(xml, "service"))
        {
            var type = ConnectXml.Text(block, "serviceType");
            if (!type.Contains(kind, StringComparison.OrdinalIgnoreCase)) continue;
            var control = ConnectXml.Text(block, "controlURL");
            if (control.Length == 0) continue;
            var resolved = Uri.TryCreate(control, UriKind.Absolute, out var absolute) ? absolute : new Uri(basis, control);
            if (resolved.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(resolved.UserInfo)) continue;
            return new DlnaService(type, resolved.ToString());
        }
        return null;
    }

    private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[262144];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(read), token).ConfigureAwait(false);
            if (count == 0) break;
            read += count;
        }
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string Header(string message, string name)
    {
        foreach (var line in message.Split('\n'))
        {
            if (!line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase)) continue;
            return line[(name.Length + 1)..].Trim().Trim('\r');
        }
        return "";
    }

    private static string Trim(string value) => value.Length <= 80 ? value : value[..80];
}
