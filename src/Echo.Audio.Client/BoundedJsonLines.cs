using System.Text;

namespace Echo.Playback.NativeAudio;

internal static class BoundedJsonLines
{
    public static async Task ReadAsync(StreamReader reader, Action<string> receive, CancellationToken token)
    {
        var buffer = new char[4096];
        var pending = new StringBuilder(4096);
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) return;
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == '\n')
                {
                    if (pending.Length > 0) receive(pending.ToString());
                    pending.Clear();
                }
                else
                {
                    if (pending.Length >= 256 * 1024) throw new IOException("Native 消息超过 256 KiB 上限");
                    pending.Append(buffer[i]);
                }
            }
        }
    }
}
