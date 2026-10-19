namespace Echo.Playback.NativeAudio;

/// <summary>UTF-8 native control framing. The receiver must consume or clone before returning.</summary>
internal static class NativeJsonLines
{
    internal const int MaximumLineBytes = 256 * 1024;

    public static async Task ReadAsync(Stream stream, Action<ReadOnlyMemory<byte>> receive, CancellationToken token)
    {
        var buffer = new byte[8192];
        var used = 0;
        while (true)
        {
            // One extra byte allows an exactly-at-limit message followed by LF.
            if (used == buffer.Length)
                Array.Resize(ref buffer, Math.Min(buffer.Length * 2, MaximumLineBytes + 1));
            var count = await stream.ReadAsync(buffer.AsMemory(used), token).ConfigureAwait(false);
            if (count == 0)
            {
                if (used != 0) throw new IOException("Native 消息不完整");
                return;
            }
            used += count;
            var consumed = 0;
            var search = used - count;
            while (true)
            {
                var next = buffer.AsSpan(search, used - search).IndexOf((byte)'\n');
                if (next < 0) break;
                var end = search + next;
                var length = end - consumed;
                if (length > MaximumLineBytes) throw new IOException("Native 消息超过 256 KiB 上限");
                if (length > 0) receive(buffer.AsMemory(consumed, length));
                consumed = search = end + 1;
            }
            if (consumed > 0)
            {
                used -= consumed;
                buffer.AsSpan(consumed, used).CopyTo(buffer);
            }
            if (used > MaximumLineBytes) throw new IOException("Native 消息超过 256 KiB 上限");
        }
    }

    public static async Task DrainAsync(Stream stream, CancellationToken token)
    {
        // stderr is intentionally discarded. No decoding, strings or retained log history.
        var buffer = new byte[4096];
        while (await stream.ReadAsync(buffer, token).ConfigureAwait(false) != 0) { }
    }
}
