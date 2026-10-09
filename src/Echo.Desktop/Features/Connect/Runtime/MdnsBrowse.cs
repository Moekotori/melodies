using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Echo.Desktop.Features.Connect.Runtime;

internal static class MdnsBrowse
{
    public static async Task<IReadOnlyList<string>> AirPlayNamesAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(1200));
        using var udp = new UdpClient();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            foreach (var query in new[] { "_airplay._tcp.local", "_raop._tcp.local" })
                await udp.SendAsync(Query(query), new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353), timeout.Token).ConfigureAwait(false);
            while (names.Count < 16)
            {
                var result = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                foreach (var name in Names(result.Buffer)) names.Add(name);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        return names.Take(16).ToArray();
    }

    private static byte[] Query(string name)
    {
        var buffer = new MemoryStream();
        buffer.Write(new byte[] { 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 });
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            buffer.WriteByte((byte)bytes.Length);
            buffer.Write(bytes);
        }
        buffer.Write(new byte[] { 0, 0, 12, 0, 1 });
        return buffer.ToArray();
    }

    private static IEnumerable<string> Names(byte[] data)
    {
        if (data.Length < 12) yield break;
        var offset = 12;
        var questions = Read16(data, 4);
        var answers = Read16(data, 6);
        for (var i = 0; i < questions && offset < data.Length; i++)
        {
            _ = ReadName(data, ref offset);
            offset += 4;
        }
        for (var i = 0; i < answers && offset + 10 < data.Length; i++)
        {
            var owner = ReadName(data, ref offset);
            if (offset + 10 > data.Length) yield break;
            var type = Read16(data, offset);
            offset += 8;
            var length = Read16(data, offset);
            offset += 2;
            var dataEnd = offset + length;
            if (type == 12 && offset < data.Length)
            {
                var target = ReadName(data, ref offset);
                var label = Label(target) ?? Label(owner);
                if (label is not null) yield return label;
            }
            offset = Math.Max(offset, dataEnd);
        }
    }

    private static string? Label(string name)
    {
        if (!name.Contains("_airplay", StringComparison.OrdinalIgnoreCase) && !name.Contains("_raop", StringComparison.OrdinalIgnoreCase)) return null;
        var label = name.Split('.')[0];
        if (label.Length is 0 or > 80 || label.StartsWith('_')) return null;
        var at = label.LastIndexOf('@');
        if (at > 0) label = label[(at + 1)..];
        return label.Length == 0 ? null : label;
    }

    private static string ReadName(byte[] data, ref int offset, int depth = 0)
    {
        var parts = new List<string>();
        while (offset < data.Length && depth < 6)
        {
            var length = data[offset++];
            if (length == 0) break;
            if ((length & 0xC0) == 0xC0)
            {
                if (offset >= data.Length) break;
                var pointer = ((length & 0x3F) << 8) | data[offset++];
                parts.Add(ReadName(data, ref pointer, depth + 1));
                break;
            }
            if (offset + length > data.Length) break;
            parts.Add(Encoding.ASCII.GetString(data, offset, length));
            offset += length;
        }
        return string.Join('.', parts.Where(part => part.Length > 0));
    }

    private static int Read16(byte[] data, int offset) => offset + 1 >= data.Length ? 0 : (data[offset] << 8) | data[offset + 1];
}
