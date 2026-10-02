using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    public bool GeneratedDsdAllowed => native is INativeOutputControl { ProDspAllowed: true };
    public JsonElement? RequestedOutput => deviceConfiguration;

    private async Task EnsureOutputQueryHostAsync()
    {
        if (initialized) return;
        // A saved unavailable output must not prevent users from choosing a replacement.
        Interlocked.Increment(ref transportEpoch);
        suppressNativeEvents = true;
        try { await native.StartAsync().ConfigureAwait(false); }
        finally { suppressNativeEvents = false; }
    }

    public async Task<IReadOnlyList<AudioOutputMode>> GetOutputModesAsync(CancellationToken token = default)
    {
        IReadOnlyList<AudioOutputMode> result = [];
        await Command(async () =>
        {
            await EnsureOutputQueryHostAsync().ConfigureAwait(false);
            var modes = new List<AudioOutputMode> { new("shared", "系统共享") };
            if (native is INativeOutputControl { Capabilities: { } caps })
            {
                if (caps.Flag("wasapiExclusive") || caps.Flag("coreAudioExclusive")) modes.Add(new("exclusive", caps.Flag("wasapiExclusive") ? "WASAPI 独占" : "CoreAudio 独占"));
                if (caps.Flag("asio")) modes.Add(new("asio", "ASIO"));
                if (caps.Flag("kernelStreaming")) modes.Add(new("ks", "WDM-KS"));
            }
            result = modes.AsReadOnly();
        }, token, affectsPlayback: false).ConfigureAwait(false);
        return result;
    }

    public async Task<IReadOnlyList<AudioOutputDevice>> GetOutputDevicesAsync(string mode, CancellationToken token = default)
    {
        IReadOnlyList<AudioOutputDevice> result = [];
        await Command(async () =>
        {
            await EnsureOutputQueryHostAsync().ConfigureAwait(false);
            if (native is not INativeOutputControl output) throw new NotSupportedException("宿主未接入输出枚举");
            result = await output.EnumerateOutputDevicesAsync(mode, token).ConfigureAwait(false);
        }, token, affectsPlayback: false).ConfigureAwait(false);
        return result;
    }

    public async Task<JsonElement> ProbeAsioDsdAsync(AudioOutputDevice device, CancellationToken token = default)
    {
        JsonElement result = default;
        await Command(async () =>
        {
            // Driver probing may reset its mode; never probe a driver while this session is playing.
            if (snapshot.OperationId != 0) throw new InvalidOperationException("先停止播放，再探测 DSD 格式");
            if (native is not INativeOutputControl output) throw new NotSupportedException("宿主未接入 DSD 探测");
            Interlocked.Increment(ref transportEpoch);
            suppressNativeEvents = true;
            try { await output.SetOutputChannelsAsync(output.OutputChannels, token, forceRestart: true).ConfigureAwait(false); initialized = false; }
            finally { suppressNativeEvents = false; }
            result = await output.ProbeAsioDsdAsync(device, token).ConfigureAwait(false);
        }, token, affectsPlayback: false).ConfigureAwait(false);
        return result;
    }

    public Task ConfigureOutputAsync(AudioOutputConfiguration configuration, CancellationToken token = default)
        => ConfigureOutputAsync(configuration.ToJson(), token);

    private static int ValidateOutputChannels(JsonElement config)
    {
        var channels = (int)config.Long("channels", 2);
        var mode = config.Text("outputMode") ?? "shared";
        var format = config.Child("processing")?.Text("outputFormat") ?? "pcm";
        if (channels is < 2 or > 8 || channels > 2 && (mode != "asio" || format != "pcm"))
            throw new ArgumentException("多声道 PCM 需要 ASIO；DSD 输出必须为双声道");
        return channels;
    }

    private async Task ApplyOutputAsync(JsonElement config)
    {
        var channels = ValidateOutputChannels(config);
        var previousMode = deviceConfiguration?.Text("outputMode") ?? "shared";
        var nextMode = config.Text("outputMode") ?? "shared";
        // Isolate driver teardown: an ASIO/KS driver may block close inside device.configure.
        var rebuild = !initialized || previousMode != nextMode || nextMode is "asio" or "ks" or "exclusive" || snapshot.Status == PlaybackStatus.Faulted;
        if (native is INativeOutputControl outputControl && (outputControl.OutputChannels != channels || rebuild))
        {
            Interlocked.Increment(ref transportEpoch);
            suppressNativeEvents = true;
            initialized = false;
            try
            {
                await outputControl.SetOutputChannelsAsync(channels, forceRestart: rebuild).ConfigureAwait(false);
                await native.StartAsync().ConfigureAwait(false);
            }
            finally { suppressNativeEvents = false; }
            await ReplayRetainedDspAsync().ConfigureAwait(false);
            initialized = true;
        }
        else if (channels != 2 && native is not INativeOutputControl) throw new NotSupportedException("宿主不支持多声道初始化");
        var accepted = await native.CallAsync("device.configure", Params(new JsonArray(JsonNode.Parse(config.GetRawText())))).ConfigureAwait(false);
        if (!accepted.Flag("accepted")) throw new IOException("输出配置未确认");
        deviceConfiguration = config;
        Publish(snapshot with { Output = accepted, Processing = accepted.Child("processing"),
            SampleRate = (int)accepted.Number("sampleRate", snapshot.SampleRate), Channels = channels });
    }

    private async Task ChangeOutputAsync(JsonElement config)
    {
        ValidateOutputChannels(config);
        var format = config.Child("processing")?.Text("outputFormat") ?? "pcm";
        if (format != "pcm" && !GeneratedDsdAllowed) throw new NotSupportedException("当前宿主未授权启用 SDM 输出");
        await EnsureOutputQueryHostAsync().ConfigureAwait(false);
        var old = deviceConfiguration ?? new AudioOutputConfiguration().ToJson();
        var track = snapshot.Current;
        var position = snapshot.PositionSeconds;
        var active = snapshot.OperationId != 0 && snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Loading or PlaybackStatus.Paused;
        var paused = snapshot.Status == PlaybackStatus.Paused;
        if (snapshot.OperationId > 0) await StopNowAsync().ConfigureAwait(false);
        try
        {
            await ApplyOutputAsync(config).ConfigureAwait(false);
            if (active && track is not null) { await SendQueueAsync(queue, track.ItemId, repeat).ConfigureAwait(false); await OpenAsync(track, position, paused).ConfigureAwait(false); }
        }
        catch (Exception error)
        {
            try
            {
                if (snapshot.OperationId > 0) await StopNowAsync().ConfigureAwait(false);
                await ApplyOutputAsync(old).ConfigureAwait(false);
                if (active && track is not null) { await SendQueueAsync(queue, track.ItemId, repeat).ConfigureAwait(false); await OpenAsync(track, position, paused).ConfigureAwait(false); }
            }
            catch (Exception rollback)
            {
                Publish(snapshot with { Status = PlaybackStatus.Faulted, OperationId = 0 });
                throw new IOException("新输出不可用，原输出恢复也失败；请选择系统共享输出后重试", new AggregateException(error, rollback));
            }
            throw new IOException("新输出不可用，已恢复原输出配置", error);
        }
    }
}
