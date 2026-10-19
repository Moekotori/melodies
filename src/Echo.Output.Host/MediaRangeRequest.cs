using System.Text;

namespace Echo.Output.Host;

internal sealed record MediaRangeRequest(string Method, string Ticket, bool Partial, long Start, long Count)
{
    public static async Task<MediaRangeRequest> ReadAsync(Stream stream, string authority, Func<string, OutputMediaTicket?> lookup, CancellationToken token)
    {
        var bytes = new byte[8192]; var used = 0; var end = -1;
        while (used < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(used), token).ConfigureAwait(false);
            if (read == 0) throw new IOException("Incomplete HTTP request.");
            used += read; end = bytes.AsSpan(0, used).IndexOf("\r\n\r\n"u8);
            if (end >= 0) break;
        }
        if (end < 0) throw new InvalidDataException("HTTP headers exceed budget.");
        var lines = Encoding.ASCII.GetString(bytes, 0, end).Split("\r\n", StringSplitOptions.None);
        var request = lines[0].Split(' ');
        if (lines.Length > 64 || request.Length != 3 || request[0] is not ("GET" or "HEAD") || request[2] != "HTTP/1.1") throw new InvalidDataException("Unsupported HTTP request.");
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var split = line.IndexOf(':');
            if (split <= 0 || !headers.TryAdd(line[..split].Trim(), line[(split + 1)..].Trim())) throw new InvalidDataException("Duplicate or invalid HTTP header.");
        }
        if (headers.GetValueOrDefault("Host") != authority || headers.ContainsKey("Transfer-Encoding") || headers.GetValueOrDefault("Content-Length", "0") != "0") throw new InvalidDataException("Invalid loopback request.");
        var pieces = request[1].Split('/');
        if (pieces.Length != 4 || pieces[1] != "media" || pieces[2].Length != 64 || !pieces[2].All(char.IsAsciiHexDigit)) throw new UnauthorizedAccessException();
        var ticket = lookup(pieces[2]) ?? throw new UnauthorizedAccessException();
        long start = 0, last = ticket.Length - 1; var partial = headers.TryGetValue("Range", out var range);
        if (partial)
        {
            if (!range!.StartsWith("bytes=", StringComparison.Ordinal) || range.Contains(',')) throw new InvalidDataException("Invalid Range.");
            var bounds = range[6..].Split('-');
            if (bounds.Length != 2) throw new InvalidDataException("Invalid Range.");
            if (bounds[0].Length == 0)
            {
                if (!long.TryParse(bounds[1], out var suffix) || suffix <= 0) throw new InvalidDataException("Invalid Range.");
                start = Math.Max(0, ticket.Length - suffix);
            }
            else
            {
                if (!long.TryParse(bounds[0], out start)) throw new InvalidDataException("Invalid Range.");
                if (bounds[1].Length != 0 && !long.TryParse(bounds[1], out last)) throw new InvalidDataException("Invalid Range.");
                last = Math.Min(last, ticket.Length - 1);
            }
        }
        if (start < 0 || start > ticket.Length || partial && (start == ticket.Length || last < start)) throw new InvalidDataException("Unsatisfiable Range.");
        return new(request[0], ticket.Id, partial, start, Math.Max(0, last - start + 1));
    }
}
