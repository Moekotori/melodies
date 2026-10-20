using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Playback;
using static Echo.Desktop.Features.Player.SignalPathFacts;

namespace Echo.Desktop.Features.Player;

// Owns one visible panel's subscription and a latest-frame slot. Closing cancels both.
public sealed partial class SignalPathViewModel : ObservableObject, IDisposable
{
    private readonly PlayerViewModel player;
    private readonly PlaybackStateSubscription subscription;
    private readonly CancellationTokenSource lifetime = new();
    private int presentationQueued;
    private PlaybackStateSnapshot latest;
    private readonly SignalPathPresentationState presentation = new();
    private SignalPathSections.Readout? sectionReadout;
    private double? sectionPeak, sectionBuffer;
    private long performancePresentedAt;
    private bool disposed;
    [ObservableProperty] private string tone = "muted";
    [ObservableProperty] private string spec = "";
    [ObservableProperty] private string headline = "";
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string layers = "";
    [ObservableProperty] private bool isModified;
    [ObservableProperty] private string levelText = "--";
    [ObservableProperty] private string levelSource = "";
    [ObservableProperty] private string levelSummary = "";
    [ObservableProperty] private string levelDetail = "";
    [ObservableProperty] private double levelPercent;
    [ObservableProperty] private double rmsPercent;
    [ObservableProperty] private bool levelClip;
    [ObservableProperty] private string diagnosis = "";
    [ObservableProperty] private IReadOnlyList<SignalPathFact> metrics = [];
    [ObservableProperty] private IReadOnlyList<SignalPathFact> outputFacts = [];
    [ObservableProperty] private IReadOnlyList<SignalPathFact> diagnostics = [];
    [ObservableProperty] private IReadOnlyList<SignalPathNode> nodes = [];
    [ObservableProperty] private IReadOnlyList<SignalPathChip> engagedChips = [];
    [ObservableProperty] private string idleSummary = "";
    [ObservableProperty] private string insightPreview = "";
    [ObservableProperty] private IReadOnlyList<SignalPathInsight> insights = [];
    [ObservableProperty] private string doctorCount = "";
    [ObservableProperty] private string stabilityHeadline = "";
    [ObservableProperty] private string stabilityDetail = "";
    [ObservableProperty] private IReadOnlyList<SignalPathFact> stability = [];
    public string Title => L("title");
    public string CloseLabel => L("close");
    public string LevelLabel => L("level");
    public string LevelNote => L("levelNote");
    public string DiagnosticsLabel => L("diagnostics");
    public string PerformanceText => player.PerformanceText;
    public string OutputSettingsLabel => L("settings");
    public string ChainLabel => L("chain");
    public string TechLabel => L("tech");
    public string StabilityLabel => L("stability");
    private string L(string key) => player.Text["signal." + key];
    private string Unknown => L("unknown");

    public SignalPathViewModel(PlayerViewModel player)
    {
        this.player = player;
        subscription = player.Zone.Subscribe(PlaybackStateChanges.Playback | PlaybackStateChanges.Output | PlaybackStateChanges.Meter | PlaybackStateChanges.Performance);
        latest = subscription.Initial;
        player.Text.Changed += RefreshLanguage;
        Present(); _ = RunAsync();
    }
    private async Task RunAsync()
    {
        try { await Task.WhenAll(ReadAsync(), RefreshAsync()).ConfigureAwait(false); }
        finally { lifetime.Dispose(); }
    }
    private async Task RefreshAsync()
    {
        using var cadence = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await cadence.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
            {
                if (Interlocked.Exchange(ref presentationQueued, 1) != 0) continue;
                Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref presentationQueued, 0); Present(); });
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task ReadAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
                Volatile.Write(ref latest, (await subscription.ReadAsync(lifetime.Token).ConfigureAwait(false)).State);
        }
        catch (OperationCanceledException) { }
        catch (System.Threading.Channels.ChannelClosedException) { }
        finally { lifetime.Cancel(); }
    }
    private void RefreshLanguage()
    {
        presentation.Invalidate();
        foreach (var property in new[] { nameof(Title), nameof(CloseLabel), nameof(LevelLabel), nameof(LevelNote), nameof(DiagnosticsLabel), nameof(OutputSettingsLabel), nameof(ChainLabel), nameof(TechLabel), nameof(StabilityLabel) })
            OnPropertyChanged(property);
        Present();
    }
    private void Present()
    {
        if (disposed) return;
        var frame = Volatile.Read(ref latest);
        var now = Stopwatch.GetTimestamp();
        if (performancePresentedAt == 0 || Stopwatch.GetElapsedTime(performancePresentedAt, now).TotalMilliseconds >= 500)
        {
            performancePresentedAt = now;
            player.RefreshPerformance(frame);
            OnPropertyChanged(nameof(PerformanceText));
        }
        PresentMeter(frame);
        var rebuild = presentation.Update(frame, player.Details.Format, player.Details.SampleRate, player.Details.BitDepth);
        if (rebuild) BuildFacts(frame);
        PresentSections(frame, rebuild);
    }
    private void PresentMeter(PlaybackStateSnapshot frame)
    {
        var level = frame.Level;
        var live = frame.Playback.Status == PlaybackStatus.Playing && level?.OperationId == frame.Playback.OperationId
            && level is not null && Stopwatch.GetElapsedTime(level.Timestamp) < TimeSpan.FromSeconds(1);
        LevelText = live ? $"{level!.PeakDb:0.0} dBFS" : "--";
        LevelPercent = live ? MeterPercent(level!.PeakDb) : 0;
        RmsPercent = live && level!.RmsDb is { } rmsDb ? MeterPercent(rmsDb) : 0;
        LevelClip = live && level!.PeakDb >= 0;
        LevelSource = live ? L("nativeMeter") : frame.Playback.Status == PlaybackStatus.Paused ? L("paused") : L("waitingMeter");
        LevelDetail = live ? Join(level!.RmsDb is { } rms ? $"RMS {rms:0.0} dBFS" : null,
            level.PeakDb >= 0 ? L("clip") : $"{L("headroom")} {-level.PeakDb:0.0} dB") : L("noMeter");
        LevelSummary = live ? Join(LevelSource, LevelDetail) : LevelSource;
    }
    private static double MeterPercent(double db) => Math.Clamp(db switch
    {
        < -24 => (db + 60) / 36 * 40,
        < -12 => 40 + (db + 24) / 12 * 20,
        < -6 => 60 + (db + 12) / 6 * 20,
        _ => 80 + (db + 6) / 6 * 20
    }, 0, 100);
    private void BuildFacts(PlaybackStateSnapshot frame)
    {
        var state = frame.Playback;
        var output = frame.Output.Confirmed;
        var processing = state.Processing;
        var src = Child(processing, "echoSrc"); var sdm = Child(processing, "sdm"); var dither = Child(processing, "dither"); var limiter = Child(processing, "limiter");
        var active = frame.Output.HasConfirmedDevice;
        var rate = Number(output, "hardwareSampleRate");
        var streamRate = Number(output, "sampleRate");
        // Playback.SampleRate is the native clock rate; never label it as file metadata.
        double? sourceRate = player.Details.SampleRate > 0 ? player.Details.SampleRate : Number(src, "sourceSampleRate");
        if (sourceRate is not > 0 && double.TryParse(state.Current?.Metadata?.GetValueOrDefault("sampleRate"), out var source)) sourceRate = source;
        var format = player.Details.Format;
        if (format.Length == 0) format = state.Current?.Metadata?.GetValueOrDefault("codec")?.ToUpperInvariant() ?? Unknown;
        var depth = player.Details.BitDepth > 0 ? $"{player.Details.BitDepth} bit" : "";
        var channels = Number(output, "channels") is > 0 ? $"{Number(output, "channels"):0} ch" : "";
        var backend = Text(output, "backend");
        var exclusive = Boolean(output, "exclusive");
        var mode = backend == "asio" ? L("asioExclusive") : backend == "wdm-ks" ? "WDM-KS"
            : exclusive is true ? L("exclusive") : exclusive is false ? L("shared") : Unknown;
        var dsp = Flag(output, "dspActive") || Flag(processing, "dspActive");
        var converted = Flag(src, "active") || Flag(sdm, "active") || Flag(dither, "active") || sourceRate is > 0 && streamRate is > 0 && sourceRate != streamRate;
        var attenuated = state.Volume < .999999;
        IsModified = active && (dsp || converted || attenuated);
        Spec = state.Current is null ? L("noTrack") : Join(format, Rate(sourceRate), depth);
        Headline = state.Status == PlaybackStatus.Faulted ? L("failed") : !active ? L("waiting") : dsp ? L("dsp") : converted ? L("converted") : attenuated ? L("volumeChanged") : L("native");
        Description = !active ? L("unconfirmed") : dsp ? L("dspOn") : converted ? L("conversionOn") : attenuated ? L("volumeOn") : L("dspOff");
        var route = dsp ? L("dspRoute") : converted ? L("conversionRoute") : L("direct");
        var device = active ? Text(output, "deviceName") ?? Unknown : Unknown;
        var clockChanged = sourceRate is > 0 && rate is > 0 && sourceRate != rate;
        var resampled = sourceRate is > 0 && streamRate is > 0 && sourceRate != streamRate;
        var clock = clockChanged ? $"{Rate(sourceRate)} → {Rate(rate)}" : Rate(rate) is { Length: > 0 } actualClock ? actualClock : Unknown;
        Metrics = [new(L("source"), Join(format, Rate(sourceRate), depth), state.Current?.Source?.Kind == "http" ? L("http") : L("local")),
            new(L("processing"), !dsp && resampled ? L("src") : route,
                resampled ? $"{Rate(sourceRate)} → {Rate(streamRate)}" : Description, active && (dsp || converted || attenuated)),
            new(L("output"), device, Join(mode, Text(output, "backendImpl") ?? Text(output, "backend"), Rate(rate))),
            new(L("clock"), clock,
                sourceRate is > 0 && rate is > 0 ? clockChanged ? L("conversionOn") : L("sameClock") : L("clockPending"), clockChanged)];
        OutputFacts = [new(L("inputFormat"), format), new(L("sent"), Join(Text(processing, "outputFormat") ?? Text(output, "format"), Rate(streamRate), channels) is { Length: > 0 } sent ? sent : Unknown),
            new(L("declared"), Unknown), new(L("ceiling"), Unknown), new(L("currentProcessing"), active ? route : Unknown),
            new(L("actualRate"), Rate(rate) is { Length: > 0 } actualRate ? actualRate : Unknown)];
        var faulted = state.Status == PlaybackStatus.Faulted;
        var shared = mode == L("shared");
        Tone = faulted ? "danger" : !active ? "muted" : dsp || converted || attenuated ? "proc" : "good";
        var chain = SignalPathNodes.Create(new()
        {
            Text = L, State = state, Output = output, Processing = processing, Format = format, Spec = Spec, SourceRate = sourceRate,
            BitDepth = player.Details.BitDepth, Active = active, Dsp = dsp, Resampled = resampled || Flag(src, "active"), Attenuated = attenuated,
            Shared = shared, Device = device, Mode = mode, Clock = clock, ClockChanged = clockChanged, Rate = rate
        });
        Nodes = chain; Layers = player.Text.Format("signal.layers", chain.Count);
        var buffer = Number(output, "actualBufferFrames") ?? Number(output, "deviceBufferFrames");
        Diagnostics = [new(L("backend"), Text(output, "backendImpl") ?? Text(output, "backend") ?? Unknown),
            new(L("streamRate"), Rate(streamRate) is { Length: > 0 } stream ? stream : Unknown), new(L("volume"), $"{state.Volume:P0}"),
            new(L("buffer"), buffer is > 0 ? $"{buffer:0} frames" : Unknown), new(L("src"), Flag(src, "active") ? Join(Rate(Number(src, "sourceSampleRate")), "→", Rate(Number(src, "targetSampleRate"))) : L("off")),
            new(L("dither"), Flag(dither, "active") ? Text(dither, "mode") ?? Unknown : L("off"))];
        Diagnosis = state.Error ?? (!active ? L("unconfirmed") : mode == L("shared") ? L("sharedNote") : dsp || converted || attenuated ? L("modifiedNote") : L("bitPerfectNote"));
        sectionReadout = new()
        {
            Text = L, Format = (key, args) => player.Text.Format(key, args), Active = active, Faulted = state.Status == PlaybackStatus.Faulted,
            Error = state.Error, Dsp = dsp, Sdm = Flag(sdm, "active"), Resampled = resampled || Flag(src, "active"),
            EchoSrc = Flag(src, "active"), DitherKnown = dither is not null, DitherActive = Flag(dither, "active"),
            LimiterKnown = limiter is not null, LimiterActive = Flag(limiter, "active"), LimiterProtecting = Flag(limiter, "protecting"),
            Attenuated = attenuated, ClockChanged = clockChanged,
            BitPerfect = (Flag(processing, "bitPerfectCandidate") || Flag(output, "bitPerfectCandidate")) && string.IsNullOrEmpty(state.Error),
            RequestedMode = Text(frame.Output.Requested, "outputMode"), Exclusive = exclusive, Mode = mode, Device = device,
            Backend = Text(output, "backendImpl") ?? Text(output, "backend"), Route = route, Clock = clock, DitherMode = Text(dither, "mode"),
            SdmBackend = Text(sdm, "activeBackend"), Volume = state.Volume, PeakDb = null,
            BufferMilliseconds = null, LatencyMilliseconds = Number(src, "nominalLatencyMilliseconds"),
            BufferFrames = Number(output, "actualBufferFrames") ?? Number(output, "deviceBufferFrames")
        };
    }
    private void PresentSections(PlaybackStateSnapshot frame, bool force)
    {
        if (sectionReadout is null) return;
        var peak = frame.Level is { } level && frame.Playback.Status == PlaybackStatus.Playing
            && level.OperationId == frame.Playback.OperationId && Stopwatch.GetElapsedTime(level.Timestamp).TotalSeconds < 1
            ? level.PeakDb : (double?)null;
        // The notes show near-clipping peaks to 0.1 dB and buffers to whole milliseconds.
        var peakKey = peak is >= -3 ? Math.Round(peak.Value, 1) : (double?)null;
        var buffer = frame.Performance.BufferedMilliseconds;
        var bufferKey = buffer is > 0 ? Math.Round(buffer.Value) : (double?)null;
        if (!force && peakKey == sectionPeak && bufferKey == sectionBuffer) return;
        sectionPeak = peakKey; sectionBuffer = bufferKey;
        sectionReadout.PeakDb = peak; sectionReadout.BufferMilliseconds = buffer;
        var sections = SignalPathSections.Create(sectionReadout);
        EngagedChips = sections.Engaged; IdleSummary = sections.IdleSummary; InsightPreview = sections.InsightPreview;
        Insights = sections.Insights; DoctorCount = sections.DoctorCount; StabilityHeadline = sections.StabilityHeadline;
        StabilityDetail = sections.StabilityDetail; Stability = sections.Stability;
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        player.Text.Changed -= RefreshLanguage; subscription.Dispose();
        try { lifetime.Cancel(); } catch (ObjectDisposedException) { }
    }
}
