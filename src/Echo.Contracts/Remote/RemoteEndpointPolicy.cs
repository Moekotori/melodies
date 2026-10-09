using System.Net;
using System.Net.Sockets;

namespace Echo.Contracts.Remote;

// Public hosts that carry a password must use HTTPS. Link-local metadata addresses are never allowed.
public static class RemoteEndpointPolicy
{
    public static void Require(Uri uri, bool hasSecret)
    {
        var host = uri.IdnHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (host is "metadata.google.internal" or "169.254.169.254")
            throw new ArgumentException("不能连接云主机元数据地址。");
        if (!hasSecret || uri.Scheme == "https" || IsPrivate(host)) return;
        throw new ArgumentException("公网地址必须使用 HTTPS。");
    }
    private static bool IsPrivate(string host)
    {
        if (host is "localhost" or "127.0.0.1" or "::1") return true;
        if (host.EndsWith(".home.arpa", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal)) return true;
        return IPAddress.TryParse(host, out var address) && IsPrivate(address);
    }
    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || (bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc);
        }
        var v4 = address.GetAddressBytes();
        return v4[0] == 10 || v4[0] == 127 || (v4[0] == 192 && v4[1] == 168) || (v4[0] == 172 && v4[1] is >= 16 and <= 31)
            || (v4[0] == 169 && v4[1] == 254 && !(v4[2] == 169 && v4[3] == 254));
    }
}
