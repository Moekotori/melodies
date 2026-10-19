using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Echo.Desktop.Features.Connect;

internal static class ConnectAddress
{
    public static bool IsHost(string host)
    {
        if (host.Length is < 1 or > 253 || host.Contains('%') || host.Contains('@') || host.Contains('/') || host.Contains('\\') || host.Contains(' '))
            return false;
        if (IPAddress.TryParse(host, out var address))
            return address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6;
        if (host.Contains(':')) return false;
        foreach (var label in host.Split('.'))
        {
            if (label.Length is < 1 or > 63 || label[0] == '-' || label[^1] == '-') return false;
            if (!label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        }
        return true;
    }

    public static bool TryPort(string text, out int port)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port) && port is >= 1 and <= 65535;

    public static bool IsHttp(string value)
    {
        if (value.Length is < 8 or > 2048 || value.Contains('\n') || value.Contains('\r')) return false;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            return false;
        var host = uri.IdnHost.Length > 0 ? uri.IdnHost : uri.Host;
        return IsHost(host);
    }

    public static bool IsTopic(string topic)
        => topic.Length is >= 1 and <= 80 && topic.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '_' or '-');

    public static string Machine()
    {
        var cleaned = new string(Environment.MachineName.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_').Take(32).ToArray());
        return cleaned.Length == 0 ? "melodies" : cleaned;
    }

    public static bool IsLoopback(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
           || IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);

    public static IReadOnlyList<string> LanIPv4()
    {
        var addresses = new List<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var entry in nic.GetIPProperties().UnicastAddresses)
            {
                if (entry.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(entry.Address)) continue;
                var text = entry.Address.ToString();
                if (text.StartsWith("169.254.", StringComparison.Ordinal) || addresses.Contains(text)) continue;
                addresses.Add(text);
                if (addresses.Count == 8) return addresses;
            }
        }
        return addresses;
    }

    public static string HostFor(string remoteHost)
    {
        var locals = LanIPv4();
        if (locals.Count == 0) return "127.0.0.1";
        if (!IPAddress.TryParse(remoteHost, out var remote) || remote.AddressFamily != AddressFamily.InterNetwork) return locals[0];
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var entry in nic.GetIPProperties().UnicastAddresses)
            {
                if (entry.Address.AddressFamily != AddressFamily.InterNetwork || entry.IPv4Mask is null) continue;
                if (SameNetwork(entry.Address, remote, entry.IPv4Mask)) return entry.Address.ToString();
            }
        }
        return locals[0];
    }

    private static bool SameNetwork(IPAddress local, IPAddress remote, IPAddress mask)
    {
        var left = local.GetAddressBytes();
        var right = remote.GetAddressBytes();
        var bits = mask.GetAddressBytes();
        if (left.Length != 4 || right.Length != 4 || bits.Length != 4) return false;
        for (var i = 0; i < 4; i++)
            if ((left[i] & bits[i]) != (right[i] & bits[i])) return false;
        return true;
    }
}
