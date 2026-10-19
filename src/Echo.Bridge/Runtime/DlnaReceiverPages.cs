using System.Net;
using System.Text;
using Echo.Playback;
using Echo.Contracts.Settings;

namespace Echo.Bridge;

internal static class DlnaReceiverPages
{
    public static byte[] Description(string name, string uuid)
    {
        var xml = "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\"><device>"
            + "<deviceType>urn:schemas-upnp-org:device:MediaRenderer:1</deviceType><friendlyName>" + ConnectXml.Escape(name) + "</friendlyName>"
            + "<manufacturer>melodies</manufacturer><modelName>melodies</modelName><UDN>uuid:" + ConnectXml.Escape(uuid) + "</UDN><serviceList>"
            + Service("AVTransport", "/upnp/av", "/upnp/av.xml")
            + Service("RenderingControl", "/upnp/rc", "/upnp/rc.xml")
            + "</serviceList></device></root>";
        return Encoding.UTF8.GetBytes(xml);
    }

    public static byte[] Scpd(string kind) => Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\"?><scpd xmlns=\"urn:schemas-upnp-org:service-1-0\"><actionList>"
        + (kind == "av"
            ? Action("SetAVTransportURI") + Action("Play") + Action("Pause") + Action("Stop") + Action("GetTransportInfo") + Action("GetPositionInfo")
            : Action("SetVolume") + Action("GetVolume"))
        + "</actionList></scpd>");

    public static byte[] Response(string service, string action, string body)
        => Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:"
            + action + "Response xmlns:u=\"" + ConnectXml.Escape(service) + "\">" + body + "</u:" + action + "Response></s:Body></s:Envelope>");

    public static byte[] Fault(string message)
        => Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultstring>"
            + ConnectXml.Escape(message) + "</faultstring></s:Fault></s:Body></s:Envelope>");

    public static string Uuid(IBridgePreferences preferences)
    {
        var saved = ConnectPreferences.Read(preferences, "connect.receive.uuid");
        if (saved.Length is >= 8 and <= 80 && saved.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return saved;
        var created = Guid.NewGuid().ToString("D");
        ConnectPreferences.Write(preferences, "connect.receive.uuid", created);
        return created;
    }

    public static string ActionName(LanRequest request)
    {
        var header = request.Header("soapaction").Trim().Trim('"');
        var hash = header.LastIndexOf('#');
        if (hash >= 0 && hash < header.Length - 1) return header[(hash + 1)..];
        foreach (var name in new[] { "SetAVTransportURI", "Play", "Pause", "Stop", "GetTransportInfo", "GetPositionInfo", "SetVolume", "GetVolume" })
            if (request.Body.AsSpan().IndexOf(Encoding.UTF8.GetBytes(name)) >= 0) return name;
        return "";
    }

    private static string Service(string name, string control, string scpd)
        => "<service><serviceType>urn:schemas-upnp-org:service:" + name + ":1</serviceType><serviceId>urn:upnp-org:serviceId:" + name
           + "</serviceId><controlURL>" + control + "</controlURL><eventSubURL>" + control + "</eventSubURL><SCPDURL>" + scpd + "</SCPDURL></service>";

    private static string Action(string name) => "<action><name>" + name + "</name></action>";
}
