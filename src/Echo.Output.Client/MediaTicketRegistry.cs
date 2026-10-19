using System.Security.Cryptography;

namespace Echo.Output.Client;

/// <summary>Core-owned capability table. No handles or media buffers are retained per queue item.</summary>
public sealed class MediaTicketRegistry
{
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<string, LinkedListNode<Entry>> ids = new(StringComparer.Ordinal);
    private readonly LinkedList<Entry> recent = new();
    private int characters;
    private sealed record Entry(OutputMediaTicket Ticket, string Path, long Stamp);
    public OutputMediaTicket Issue(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Length > 4096) throw new ArgumentException("Output 只接受 Core 签发的本地媒体。");
        path = Path.GetFullPath(path); var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Core 音源不存在。");
        lock (gate)
        {
            if (paths.TryGetValue(path, out var old))
            {
                if (old.Value.Stamp == info.LastWriteTimeUtc.Ticks && old.Value.Ticket.Length == info.Length)
                { recent.Remove(old); recent.AddLast(old); return old.Value.Ticket; }
                Remove(old);
            }
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension.Length > 16 || extension.Any(value => value != '.' && !char.IsAsciiLetterOrDigit(value))) extension = ".bin";
            var ticket = new OutputMediaTicket(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), info.Length, extension);
            var node = recent.AddLast(new Entry(ticket, path, info.LastWriteTimeUtc.Ticks));
            paths.Add(path, node); ids.Add(ticket.Id, node); characters += path.Length;
            while (ids.Count > OutputProtocol.MaximumTickets || characters > 262144) Remove(recent.First!);
            return ticket;
        }
    }
    private void Remove(LinkedListNode<Entry> node)
    { recent.Remove(node); paths.Remove(node.Value.Path); ids.Remove(node.Value.Ticket.Id); characters -= node.Value.Path.Length; }
    public bool Contains(string id) { lock (gate) return ids.ContainsKey(id); }
    public string? PathFor(string id) { lock (gate) return ids.TryGetValue(id, out var node) ? node.Value.Path : null; }
    public FileStream Open(string id)
    {
        Entry entry;
        lock (gate) entry = ids.TryGetValue(id, out var node) ? node.Value : throw new UnauthorizedAccessException();
        var file = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.Read, OutputProtocol.StreamBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (file.Length == entry.Ticket.Length && File.GetLastWriteTimeUtc(entry.Path).Ticks == entry.Stamp) return file;
        file.Dispose(); throw new IOException("Core 音源已变更，请重新打开。");
    }
    public void Clear() { lock (gate) { paths.Clear(); ids.Clear(); recent.Clear(); characters = 0; } }
}
