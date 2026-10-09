using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Echo.Infrastructure.Radio;

public static class DnsSrvQuery
{
    public static async Task<IReadOnlyList<string>> LookupAsync(string name, CancellationToken token)
    {
        var question = Question(name, out var id);
        foreach (var server in Nameservers().Take(3))
        {
            try
            {
                using var udp = new UdpClient(server.AddressFamily);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await udp.SendAsync(question, new IPEndPoint(server, 53), timeout.Token).ConfigureAwait(false);
                var reply = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                if (reply.Buffer.Length < 12 || reply.Buffer[0] != id[0] || reply.Buffer[1] != id[1] || (reply.Buffer[2] & 0x02) != 0)
                    continue;
                var targets = ReadTargets(reply.Buffer);
                if (targets.Count > 0) return targets;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception error) when (error is SocketException or IOException) { }
        }
        return [];
    }

    public static IReadOnlyList<string> ReadTargets(ReadOnlySpan<byte> message)
    {
        if (message.Length < 12) return [];
        var count = ReadUInt16(message, 4);
        var answers = Math.Min(ReadUInt16(message, 6), 32);
        var offset = 12;
        for (var index = 0; index < count; index++)
        {
            if (!SkipName(message, ref offset) || offset + 4 > message.Length) return [];
            offset += 4;
        }
        var targets = new List<string>();
        for (var index = 0; index < answers; index++)
        {
            if (!SkipName(message, ref offset) || offset + 10 > message.Length) break;
            var type = ReadUInt16(message, offset);
            var length = ReadUInt16(message, offset + 8);
            offset += 10;
            if (offset + length > message.Length) break;
            if (type == 33 && length >= 6)
            {
                var cursor = offset + 6;
                var target = ReadName(message, ref cursor);
                if (target.Length > 0) targets.Add(target);
            }
            offset += length;
        }
        return targets;
    }

    private static byte[] Question(string name, out byte[] id)
    {
        id = [(byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256)];
        var labels = name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var size = 12 + 5;
        foreach (var label in labels) size += label.Length + 1;
        var bytes = new byte[size];
        bytes[0] = id[0]; bytes[1] = id[1]; bytes[2] = 1;
        bytes[5] = 1;
        var offset = 12;
        foreach (var label in labels)
        {
            bytes[offset++] = (byte)label.Length;
            foreach (var character in label) bytes[offset++] = (byte)character;
        }
        bytes[offset + 2] = 33;
        bytes[offset + 4] = 1;
        return bytes;
    }

    private static IReadOnlyList<IPAddress> Nameservers()
    {
        var servers = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                foreach (var address in nic.GetIPProperties().DnsAddresses)
                    if (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                        servers.Add(address);
            }
        }
        catch (NetworkInformationException) { }
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            try
            {
                foreach (var line in File.ReadLines("/etc/resolv.conf"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && parts[0] == "nameserver" && IPAddress.TryParse(parts[1], out var address))
                        servers.Add(address);
                }
            }
            catch (IOException) { }
        }
        if (servers.Count == 0)
        {
            servers.Add(IPAddress.Parse("1.1.1.1"));
            servers.Add(IPAddress.Parse("8.8.8.8"));
        }
        return servers.Distinct().Take(3).ToArray();
    }

    private static bool SkipName(ReadOnlySpan<byte> message, ref int offset) => ReadName(message, ref offset).Length >= 0;

    private static string ReadName(ReadOnlySpan<byte> message, ref int offset)
    {
        var labels = new List<string>();
        var hops = 0;
        var end = -1;
        while (offset < message.Length && hops++ < 8)
        {
            var length = message[offset];
            if (length == 0) { offset++; break; }
            if ((length & 0xC0) == 0xC0)
            {
                if (offset + 1 >= message.Length) return "";
                if (end < 0) end = offset + 2;
                offset = ((length & 0x3F) << 8) | message[offset + 1];
                continue;
            }
            offset++;
            if (length > 63 || offset + length > message.Length) return "";
            labels.Add(System.Text.Encoding.ASCII.GetString(message.Slice(offset, length)));
            offset += length;
        }
        if (end >= 0) offset = end;
        return string.Join('.', labels).TrimEnd('.');
    }

    private static int ReadUInt16(ReadOnlySpan<byte> message, int offset)
        => offset + 1 >= message.Length ? 0 : (message[offset] << 8) | message[offset + 1];
}
