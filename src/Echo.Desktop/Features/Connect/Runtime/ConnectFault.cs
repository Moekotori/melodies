using System.Net.Http;
using System.Net.Sockets;

namespace Echo.Desktop.Features.Connect.Runtime;

internal static class ConnectFault
{
    public static bool Expected(Exception error)
        => error is IOException or SocketException or InvalidOperationException or ArgumentException or TimeoutException or HttpRequestException or System.ComponentModel.Win32Exception;
}
