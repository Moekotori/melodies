using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback.NativeAudio;

public sealed partial class NativeAudioHost : INativeOutputControl
{
    public int OutputChannels { get; private set; } = 2;
    public bool ProDspAllowed => allowProDsp;
    private bool restarting;
    private TaskCompletionSource bootstrapReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task SetOutputChannelsAsync(int channels, CancellationToken token = default, bool forceRestart = false)
    {
        if (channels is < 2 or > 8) throw new ArgumentOutOfRangeException(nameof(channels));
        if (channels == OutputChannels && !forceRestart) return;
        restarting = true;
        try
        {
            if (process is { HasExited: false })
            {
                try { await CallAsync("rpc.shutdown", cancellationToken: token).WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); }
                catch (Exception) { }
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(token).ConfigureAwait(false);
            }
            foreach (var task in new[] { readerTask, stderrTask })
                if (task is not null) try { await task.ConfigureAwait(false); } catch (Exception) { }
            process?.Dispose(); process = null;
            Interlocked.Exchange(ref started, 0);
            OutputChannels = channels;
            Capabilities = null;
            HostIdentity = null;
            foreach (var request in pending)
                if (pending.TryRemove(request.Key, out var completion)) completion.TrySetException(new IOException("输出声道布局正在重建"));
        }
        finally { restarting = false; }
    }
    public Task<IReadOnlyList<AudioOutputDevice>> EnumerateOutputDevicesAsync(string mode, CancellationToken token = default)
        => NativeOutputDiscovery.DevicesAsync(executable, mode, token);
    public Task<JsonElement> ProbeAsioDsdAsync(AudioOutputDevice device, CancellationToken token = default)
        => NativeOutputDiscovery.ProbeAsync(executable, device, token);
}
