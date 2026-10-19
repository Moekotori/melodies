using System.Text.Json;

namespace Echo.Playback.NativeAudio;

public sealed partial class NativeAudioHost
{
    private int requestCount;

    public async Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        if (process is null || process.HasExited) throw new IOException("音频宿主未运行");
        if (Interlocked.Increment(ref requestCount) > 64)
        {
            Interlocked.Decrement(ref requestCount);
            throw new IOException("Native RPC 待处理请求已满");
        }
        var id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await WriteRequestAsync(id, method, parameters, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            pending.TryRemove(id, out _);
            Interlocked.Decrement(ref requestCount);
        }
    }

    private async Task WriteRequestAsync(long id, string method, JsonElement? parameters, CancellationToken token)
    {
        // Serialize only after admission to the writer. At most one encoded request
        // is held, and its buffer is released before waiting for the native reply.
        await writes.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var bytes = new MemoryStream();
            using (var writer = new Utf8JsonWriter(bytes))
            {
                writer.WriteStartObject(); writer.WriteString("jsonrpc", "2.0"); writer.WriteNumber("id", id);
                writer.WriteString("method", method);
                if (parameters is { } p) { writer.WritePropertyName("params"); p.WriteTo(writer); }
                writer.WriteEndObject();
            }
            if (bytes.Length > 256 * 1024) throw new ArgumentException("Native RPC 请求超过 256 KiB 上限");
            // JSON is already UTF-8. Keep it in bytes instead of allocating a UTF-16
            // string and asking StreamWriter to encode the whole request again.
            bytes.WriteByte((byte)'\n');
            await process!.StandardInput.BaseStream.WriteAsync(
                bytes.GetBuffer().AsMemory(0, (int)bytes.Length), token).ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(token).ConfigureAwait(false);
        }
        finally { writes.Release(); }
    }

}
