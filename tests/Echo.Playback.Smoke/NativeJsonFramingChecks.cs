using System.Text;
using System.Text.Json;
using Echo.Playback.NativeAudio;

internal static class NativeJsonFramingChecks
{
    public static async Task RunAsync()
    {
        var values = new List<JsonElement>();
        using (var input = new FragmentedStream(Encoding.UTF8.GetBytes("\n{\"name\":\"音乐🎵\"}\r\n{\"name\":\"次曲\"}\n")))
            await NativeJsonLines.ReadAsync(input, bytes =>
            {
                using var document = JsonDocument.Parse(bytes);
                values.Add(document.RootElement.Clone());
            }, default);
        if (values.Count != 2 || values[0].GetProperty("name").GetString() != "音乐🎵"
            || values[1].GetProperty("name").GetString() != "次曲")
            throw new Exception("Fragmented UTF-8 / multiple messages / cloned ownership failed");

        var limit = NativeJsonLines.MaximumLineBytes;
        var exact = Enumerable.Repeat((byte)'x', limit + 1).ToArray();
        exact[^1] = (byte)'\n';
        var received = 0;
        using (var input = new FragmentedStream(exact))
            await NativeJsonLines.ReadAsync(input, bytes => received = bytes.Length, default);
        if (received != limit) throw new Exception("Exactly-at-limit native message was rejected");
        await RejectedAsync(Enumerable.Repeat((byte)'x', limit + 1).ToArray());
        await RejectedAsync("{\"unfinished\":true}"u8.ToArray());
        Console.WriteLine("PASS native fragmented UTF-8, framing, ownership, 256 KiB bound and truncated EOF");
    }

    private static async Task RejectedAsync(byte[] bytes)
    {
        using var input = new FragmentedStream(bytes);
        try { await NativeJsonLines.ReadAsync(input, _ => { }, default); }
        catch (IOException) { return; }
        throw new Exception("Invalid native framing was accepted");
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        private int read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            var length = (++read % 4) switch { 0 => 8192, 1 => 1, 2 => 2, _ => 5 };
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, length)], token);
        }
    }
}
