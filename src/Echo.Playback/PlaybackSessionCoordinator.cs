using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

/// <summary>Single owner of native command order, queue identity and confirmed playback state.</summary>
public sealed partial class PlaybackSessionCoordinator : IPlaybackSession
{
    private readonly INativeAudio native;
    private readonly IPlaybackSourceProbe? sourceProbe;
    private readonly IPlaybackFileResolver? fileResolver;
    private IRemoteAudioResolver? remoteResolver;
    private AudioSource? currentPlayableSource;
    private readonly PlaybackCommandLane lane = new();
    private QueueTrack[] queue = [];
    private IReadOnlyList<QueueTrack> queueView = Array.Empty<QueueTrack>();
    private long revision;
    private double positionBase;
    private bool initialized, disposed;
    private RepeatMode repeat;
    private JsonElement? deviceConfiguration;
    private readonly Dictionary<string, JsonElement> dspCommands = new();
    private PlaybackSnapshot snapshot = new(0, PlaybackStatus.Stopped, null, 0, 0, 1, 0, 48000, 2, null, null, null);
    public PlaybackSnapshot Snapshot => Volatile.Read(ref snapshot);
    public IReadOnlyList<QueueTrack> Queue => Volatile.Read(ref queueView);
    public RepeatMode Repeat => repeat;
    public event Action<PlaybackSnapshot>? Changed;

    public PlaybackSessionCoordinator(INativeAudio native, IPlaybackSourceProbe? sourceProbe = null, IPlaybackFileResolver? fileResolver = null)
    {
        this.native = native;
        this.sourceProbe = sourceProbe; this.fileResolver = fileResolver;
        InitializeState();
        native.Notification += OnNotification;
        native.Failed += OnFailed;
    }
    public void AttachRemoteResolver(IRemoteAudioResolver resolver) => remoteResolver = resolver;

    private Task Command(Func<Task> action, CancellationToken token = default, bool affectsPlayback = true)
        => lane.InvokeAsync(async () =>
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try { await action().ConfigureAwait(false); }
            catch (Exception e)
            {
                Publish(snapshot with { Status = affectsPlayback ? PlaybackStatus.Faulted : snapshot.Status, Error = e.Message });
                throw;
            }
        }, token);

    private async Task EnsureStartedAsync()
    {
        if (initialized) return;
        Interlocked.Increment(ref transportEpoch);
        suppressNativeEvents = true;
        try
        {
            if (native is INativeOutputControl outputControl && deviceConfiguration is { } desired)
                await outputControl.SetOutputChannelsAsync((int)desired.Long("channels", 2)).ConfigureAwait(false);
            await native.StartAsync().ConfigureAwait(false);
        }
        finally { suppressNativeEvents = false; }
        if (deviceConfiguration is { } configuration)
        {
            var output = await native.CallAsync("device.configure", Params(new JsonArray(JsonNode.Parse(configuration.GetRawText())))).ConfigureAwait(false);
            if (!output.Flag("accepted")) throw new IOException("无法恢复输出设备设置");
            Publish(snapshot with { Output = output, SampleRate = (int)output.Number("sampleRate", snapshot.SampleRate),
                Channels = (int)output.Number("channels", snapshot.Channels), Processing = output.Child("processing") });
        }
        await ReplayRetainedDspAsync().ConfigureAwait(false);
        initialized = true;
    }

    private async Task<AudioSource> ResolvePlayableAsync(QueueTrack track)
    {
        var source = AudioSource.Playable(track);
        if (source.Kind != "remote") return source;
        if (remoteResolver is null) throw new IOException("网盘播放服务尚未装配。");
        var resolved = await remoteResolver.ResolveAsync(source, CancellationToken.None).ConfigureAwait(false);
        resolved.Validate();
        if (resolved.Kind is not ("http" or "local")) throw new IOException("网盘曲目没有解析出可播放地址。");
        return resolved;
    }

    private async Task OpenAsync(QueueTrack track, double start = 0, bool paused = false)
    {
        // Reopening even the same item creates a new native operation, so its old loop cannot survive.
        if (segmentLoop is not null)
        {
            SetSegmentLoop(null);
            await SendQueueAsync(queue, track.ItemId, repeat).ConfigureAwait(false);
        }
        var live = track.Metadata?.GetValueOrDefault("isLiveStream") == "true";
        if (live) start = 0;
        Publish(snapshot with { Current = track, Status = PlaybackStatus.Loading, Error = null, OperationId = 0 });
        var ready = await native.CallAsync("audio.sessionBegin", Params(new JsonObject
        {
            ["sr"] = snapshot.SampleRate, ["ch"] = deviceConfiguration?.Long("channels", 2) ?? 2,
            ["buffer"] = deviceConfiguration?.Long("bufferSize", 1024) ?? 1024,
            ["fifoMs"] = 8000, ["prebufferMs"] = 60, ["startPaused"] = true,
            ["initialPlayGate"] = !paused && (deviceConfiguration?.Child("processing")?.Text("outputFormat") ?? "pcm") == "pcm"
        })).ConfigureAwait(false);
        if (!ready.Flag("accepted") || ready.Long("sessionId") <= 0) throw new IOException("Native 会话未确认");
        var output = ready.Child("ready");
        var requestedChannels = deviceConfiguration?.Long("channels", 2) ?? 2;
        if (output is { } facts && facts.Long("channels", requestedChannels) != requestedChannels)
            throw new IOException("输出设备未提供请求的完整声道范围");
        var rate = (int)(output?.Number("sampleRate", snapshot.SampleRate) ?? snapshot.SampleRate);
        await native.CallAsync("audio.setVolume", Params(new JsonArray(new JsonObject { ["volume"] = snapshot.Volume }))).ConfigureAwait(false);
        var source = await ResolvePlayableAsync(track).ConfigureAwait(false);
        var request = new JsonObject { ["sampleRate"] = rate, ["startSeconds"] = start };
        string method;
        if (source.Kind == "local" && source.Range is null) { method = "audio.openFile"; request["filePath"] = source.Uri; }
        else { method = "audio.openSource"; request["source"] = SourceJson(source); }
        var opened = await native.CallAsync(method, Params(new JsonArray(request))).ConfigureAwait(false);
        var op = opened.Long("operationId");
        if (op <= 0) throw new IOException("Native 未返回 operationId");
        currentPlayableSource = source;
        RememberSourceRate(track, (int)opened.Long("sourceSampleRate", track.SampleRate ?? 0));
        var confirmation = ArmOutputConfirmation(op);
        positionBase = opened.Number("startSeconds");
        Publish(snapshot with { OperationId = op, PositionSeconds = positionBase, DurationSeconds = live ? 0 : opened.Number("durationSeconds"),
            SampleRate = (int)opened.Number("sampleRate", rate), Channels = (int)(output?.Long("channels", requestedChannels) ?? requestedChannels),
            Output = output, Status = paused ? PlaybackStatus.Paused : PlaybackStatus.Loading });
        // Bind the selected occurrence to the acknowledged open, including when
        // multiple queue entries reference the same file on shared output.
        await SendQueueAsync(queue, track.ItemId, repeat).ConfigureAwait(false);
        await TryPrepareAlbumGaplessAsync(track).ConfigureAwait(false);
        if (!paused)
        {
            await AcceptedAsync("audio.play").ConfigureAwait(false);
            if (deviceConfiguration?.Text("outputMode") is "asio" or "exclusive" or "ks")
                await ConfirmProfessionalOutputAsync(confirmation).ConfigureAwait(false);
        }
    }

    public Task PauseAsync(CancellationToken token = default) => Command(async () =>
    {
        if (snapshot.OperationId == 0) return;
        await AcceptedAsync("audio.pause").ConfigureAwait(false);
        Publish(snapshot with { Status = PlaybackStatus.Paused });
    }, token);

    public Task ResumeAsync(CancellationToken token = default) => Command(async () =>
    {
        if (snapshot.Current is { } live && live.Metadata?.GetValueOrDefault("isLiveStream") == "true"
            && snapshot.Status is PlaybackStatus.Paused or PlaybackStatus.Faulted)
        {
            await EnsureStartedAsync().ConfigureAwait(false);
            await SendQueueAsync(queue, live.ItemId, repeat).ConfigureAwait(false);
            await OpenAsync(live).ConfigureAwait(false); return;
        }
        if ((snapshot.Status == PlaybackStatus.Ended || snapshot.OperationId == 0) && (snapshot.Current ?? ResolveNext()) is { } track)
        {
            await EnsureStartedAsync().ConfigureAwait(false);
            await SendQueueAsync(queue, track.ItemId, repeat).ConfigureAwait(false);
            await OpenAsync(track, snapshot.Status == PlaybackStatus.Paused ? snapshot.PositionSeconds : 0).ConfigureAwait(false);
            return;
        }
        if (snapshot.OperationId == 0 || snapshot.Status == PlaybackStatus.Faulted) return;
        if (pausedEofOperation == snapshot.OperationId)
        {
            Publish(snapshot with { Status = PlaybackStatus.Loading });
            await HandleNativeEofAsync(snapshot.OperationId).ConfigureAwait(false);
            return;
        }
        if (snapshot.Status == PlaybackStatus.Playing) return;
        ArmResumeConfirmation();
        await AcceptedAsync("audio.resume").ConfigureAwait(false);
        Publish(snapshot with { Status = PlaybackStatus.Loading });
    }, token);

    public Task SeekAsync(double seconds, CancellationToken token = default)
    {
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (Snapshot.Current?.Metadata?.GetValueOrDefault("isLiveStream") == "true")
            return Task.FromException(new InvalidOperationException("直播电台不支持拖动进度，请重新连接以回到直播。"));
        return Command(async () =>
        {
            if (snapshot.OperationId == 0) return;
            var position = snapshot.DurationSeconds > 0 ? Math.Min(seconds, Math.Max(0, snapshot.DurationSeconds - 0.25)) : seconds;
            if (segmentLoop is { } loop && (position < loop.StartSeconds || position >= loop.EndSeconds))
                await ClearSegmentLoopNowAsync().ConfigureAwait(false);
            await SeekNowAsync(position).ConfigureAwait(false);
        }, token);
    }

    public Task StopAsync(CancellationToken token = default) => Command(StopNowAsync, token);
    private async Task StopNowAsync()
    {
        if (initialized) await native.CallAsync("audio.stop", OperationParams()).ConfigureAwait(false);
        if (segmentLoop is not null) SetSegmentLoop(null);
        positionBase = 0;
        Publish(snapshot with { Status = PlaybackStatus.Stopped, OperationId = 0, PositionSeconds = 0, Error = null });
    }

    private JsonElement OperationParams(JsonObject? value = null)
    { value ??= new(); if (snapshot.OperationId > 0) value["expectedOperationId"] = snapshot.OperationId; return Params(new JsonArray(value)); }
    private async Task AcceptedAsync(string method)
    { if ((await native.CallAsync(method, OperationParams()).ConfigureAwait(false)).ValueKind != JsonValueKind.True) throw new IOException($"Native 拒绝 {method}"); }
    internal static JsonElement Params(JsonNode node)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) node.WriteTo(writer);
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
    private static JsonObject SourceJson(AudioSource source)
    {
        var node = new JsonObject { ["kind"] = source.Kind, ["uri"] = source.Uri };
        if (source.Range is { } r) node["range"] = new JsonObject { ["startSeconds"] = r.StartSeconds, ["endSeconds"] = r.EndSeconds };
        if (source.Headers is { } headers) { var h = new JsonObject(); foreach (var item in headers) h[item.Key] = item.Value; node["headers"] = h; }
        return node;
    }
    private void Publish(PlaybackSnapshot state)
    {
        if (state.Current?.ItemId != snapshot.Current?.ItemId
            || state.Status is PlaybackStatus.Stopped or PlaybackStatus.Ended or PlaybackStatus.Faulted) currentPlayableSource = null;
        var previousItem = snapshot.Current?.ItemId;
        if (segmentLoop is { } loop && (loop.ItemId != state.Current?.ItemId
            || state.Status is PlaybackStatus.Stopped or PlaybackStatus.Ended or PlaybackStatus.Faulted))
            SetSegmentLoop(null);
        Volatile.Write(ref snapshot, state with { Version = snapshot.Version + 1 });
        if (previousItem != state.Current?.ItemId || !ReferenceEquals(queueSnapshot.Items, queueView)
            || queueSnapshot.Mode != QueueMode) PublishQueue();
        else PublishState();
        if (Changed is { } handlers)
            foreach (Action<PlaybackSnapshot> handler in handlers.GetInvocationList()) try { handler(snapshot); } catch (Exception) { /* A UI subscriber cannot stop audio. */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        native.Notification -= OnNotification; native.Failed -= OnFailed;
        try
        {
            await lane.InvokeAsync(async () =>
            {
                disposed = true;
                CancelAutomixAnalysis();
                PublishState();
                try
                {
                    try { await native.DisposeAsync().ConfigureAwait(false); }
                    finally { if (sourceProbe is not null) await sourceProbe.DisposeAsync().ConfigureAwait(false); }
                    Publish(snapshot with { Status = PlaybackStatus.Stopped, OperationId = 0, Output = null, Processing = null });
                }
                catch
                {
                    Publish(snapshot with { Status = PlaybackStatus.Faulted, OperationId = 0, Output = null, Processing = null,
                        Error = "音频核心释放失败" });
                    throw;
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            await lane.DisposeAsync().ConfigureAwait(false);
            Changed = null;
            QueueChanged = null;
            SegmentLoopChanged = null;
            VolumeLockChanged = null;
            CloseStateSubscriptions();
        }
    }
}
