using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    public Task SetVolumeAsync(double volume, CancellationToken token = default)
    {
        if (!double.IsFinite(volume) || volume is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(volume));
        return Command(async () =>
        {
            if (volumeLocked) throw new InvalidOperationException("音量已锁定，请先在播放设置中解锁。");
            await EnsureStartedAsync().ConfigureAwait(false);
            await native.CallAsync("audio.setVolume", Params(new JsonArray(new JsonObject { ["volume"] = volume }))).ConfigureAwait(false);
            Publish(snapshot with { Volume = volume });
        }, token, affectsPlayback: false);
    }

    public async Task<JsonElement> GetDevicesAsync(CancellationToken token = default)
    {
        JsonElement result = default;
        await Command(async () => { await EnsureStartedAsync().ConfigureAwait(false); result = await native.CallAsync("device.enumerate").ConfigureAwait(false); }, token, affectsPlayback: false).ConfigureAwait(false);
        return result;
    }

    /// <summary>Native device.configure payload: deviceId, outputMode, sampleRate, channels, bufferSize, processing.</summary>
    public Task ConfigureOutputAsync(JsonElement configuration, CancellationToken token = default)
    {
        if (configuration.ValueKind != JsonValueKind.Object) throw new ArgumentException("设备配置必须是对象");
        var payload = configuration.Clone();
        return Command(async () =>
        {
            await ChangeOutputAsync(payload).ConfigureAwait(false);
        }, token, affectsPlayback: false);
    }

    // Only expose native DSP methods; no arbitrary process/filesystem/network control.
    private static bool IsDspMethod(string method) => method.StartsWith("eq.", StringComparison.Ordinal)
        || method.StartsWith("channelBalance.", StringComparison.Ordinal) || method.StartsWith("roomCorrection.", StringComparison.Ordinal)
        || method.StartsWith("crossfeed.", StringComparison.Ordinal) || method.StartsWith("compressor.", StringComparison.Ordinal)
        || method.StartsWith("stereoField.", StringComparison.Ordinal) || method.StartsWith("channelMatrix.", StringComparison.Ordinal)
        || method.StartsWith("dspRack.", StringComparison.Ordinal) || method.StartsWith("replayGain.", StringComparison.Ordinal)
        || method.StartsWith("dsp.", StringComparison.Ordinal) || method.StartsWith("profile.", StringComparison.Ordinal)
        || method.StartsWith("preset.", StringComparison.Ordinal) || method == "playbackRate.configure"
        || method is "multiband.getState" or "multiband.setState"
            or "deesser.getState" or "deesser.setState" or "deesser.setListen"
            or "virtualStage.getState" or "virtualStage.setState"
            or "headphoneCorrection.getState" or "headphoneCorrection.setState"
            or "workshopAudioEffect.getState" or "workshopAudioEffect.setState";

    public async Task<JsonElement> DspAsync(string method, JsonElement parameters, bool retainForRecovery = false, CancellationToken token = default)
    {
        if (!IsDspMethod(method)) throw new ArgumentException("不是支持的 DSP 控制方法");
        DspBandValidation.Validate(method, parameters);
        JsonElement result = default;
        await Command(async () =>
        {
            await EnsureStartedAsync().ConfigureAwait(false);
            parameters = await AdaptDspRequestAsync(method, parameters).ConfigureAwait(false);
            result = await native.CallAsync(method, parameters).ConfigureAwait(false);
            if (method == "playbackRate.configure")
            {
                ConfirmPlaybackRate(result);
                parameters = Params(new JsonArray(JsonNode.Parse(result.GetRawText())));
            }
            if (retainForRecovery)
            {
                var request = parameters.ValueKind == JsonValueKind.Array && parameters.GetArrayLength() > 0 ? parameters[0] : parameters;
                var key = method + ":" + (request.Text("bandId") ?? request.Long("band", request.Long("index", -1)).ToString());
                if (!dspCommands.ContainsKey(key) && dspCommands.Count >= 128) throw new IOException("DSP 恢复状态达到 128 项上限");
                dspCommands[key] = parameters.Clone();
            }
        }, token, affectsPlayback: false).ConfigureAwait(false);
        return result;
    }

    // Read one acknowledged, bandless DSP setting on the same owner lane.
    public async Task<JsonElement?> GetRetainedDspRequestAsync(string method, CancellationToken token = default)
    {
        if (!IsDspMethod(method)) throw new ArgumentException("不是支持的 DSP 控制方法");
        JsonElement? result = null;
        await lane.InvokeAsync(() =>
        {
            token.ThrowIfCancellationRequested();
            if (dspCommands.TryGetValue(method + ":-1", out var value)) result = value.Clone();
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        return result;
    }

    public Task RecoverAsync(CancellationToken token = default) => Command(async () =>
    {
        // Explicit user recovery reopens at the last native position; never silently skips a failure.
        var track = snapshot.Current;
        var position = snapshot.PositionSeconds;
        if (snapshot.Status == PlaybackStatus.Faulted && native is INativeOutputControl output)
        {
            Interlocked.Increment(ref transportEpoch);
            suppressNativeEvents = true;
            try { await output.SetOutputChannelsAsync(output.OutputChannels, forceRestart: true).ConfigureAwait(false); initialized = false; }
            finally { suppressNativeEvents = false; }
        }
        await EnsureStartedAsync().ConfigureAwait(false);
        if (deviceConfiguration is { } config)
        {
            var result = await native.CallAsync("device.configure", Params(new JsonArray(JsonNode.Parse(config.GetRawText())))).ConfigureAwait(false);
            if (!result.Flag("accepted")) throw new IOException("无法恢复输出设备");
        }
        await ReplayRetainedDspAsync().ConfigureAwait(false);
        await SendQueueAsync(queue, track?.ItemId, repeat).ConfigureAwait(false);
        if (track is not null) await OpenAsync(track, position).ConfigureAwait(false);
    }, token);
}
