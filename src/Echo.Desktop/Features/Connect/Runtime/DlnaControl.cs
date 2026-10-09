using System.Net.Http.Headers;
using System.Text;

namespace Echo.Desktop.Features.Connect.Runtime;

internal static class DlnaControl
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(3) })
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    public static Task SetUriAsync(DlnaRenderer device, string uri, string metadata, CancellationToken token)
        => CallAsync(device.AvTransport, "SetAVTransportURI", [("InstanceID", "0"), ("CurrentURI", uri), ("CurrentURIMetaData", metadata)], token);

    public static Task PlayAsync(DlnaRenderer device, CancellationToken token)
        => CallAsync(device.AvTransport, "Play", [("InstanceID", "0"), ("Speed", "1")], token);

    public static Task PauseAsync(DlnaRenderer device, CancellationToken token)
        => CallAsync(device.AvTransport, "Pause", [("InstanceID", "0")], token);

    public static Task StopAsync(DlnaRenderer device, CancellationToken token)
        => CallAsync(device.AvTransport, "Stop", [("InstanceID", "0")], token);

    public static Task SetVolumeAsync(DlnaRenderer device, int volume, CancellationToken token)
    {
        if (device.Rendering is null) throw new InvalidOperationException("这台设备没有音量控制。");
        return CallAsync(device.Rendering, "SetVolume", [("InstanceID", "0"), ("Channel", "Master"), ("DesiredVolume", Math.Clamp(volume, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture))], token);
    }

    private static async Task CallAsync(DlnaService service, string action, IReadOnlyList<(string Name, string Value)> args, CancellationToken token)
    {
        var body = Envelope(service.ServiceType, action, args);
        using var request = new HttpRequestMessage(HttpMethod.Post, service.ControlUrl);
        request.Content = new StringContent(body, Encoding.UTF8, "text/xml");
        request.Headers.TryAddWithoutValidation("SOAPAction", "\"" + service.ServiceType + "#" + action + "\"");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        var xml = await ReadAsync(response, token).ConfigureAwait(false);
        var fault = ConnectXml.Text(xml, "faultstring");
        if (fault.Length > 0) throw new InvalidOperationException(fault.Length > 180 ? fault[..180] : fault);
        if (!response.IsSuccessStatusCode || !xml.Contains(action + "Response", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(deviceMessage(response.StatusCode));
    }

    private static string deviceMessage(System.Net.HttpStatusCode status)
        => "设备没有接受这个播放命令（" + ((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture) + "）。";

    private static string Envelope(string serviceType, string action, IReadOnlyList<(string Name, string Value)> args)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        builder.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body>");
        builder.Append("<u:").Append(action).Append(" xmlns:u=\"").Append(ConnectXml.Escape(serviceType)).Append("\">");
        foreach (var arg in args)
            builder.Append('<').Append(arg.Name).Append('>').Append(ConnectXml.Escape(arg.Value)).Append("</").Append(arg.Name).Append('>');
        builder.Append("</u:").Append(action).Append("></s:Body></s:Envelope>");
        return builder.ToString();
    }

    private static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[65536];
        var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }
}
