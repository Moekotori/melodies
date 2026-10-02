using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Playback;

namespace Echo.Desktop.Features.Player;

public sealed record OutputFormatChoice(string Key, string Label);

public sealed partial class PlayerAudioSettingsViewModel(PlaybackSessionCoordinator session) : ObservableObject
{
    [ObservableProperty] private IReadOnlyList<AudioOutputDevice> devices = [];
    [ObservableProperty] private AudioOutputDevice? selectedDevice;
    [ObservableProperty] private IReadOnlyList<AudioOutputMode> modes = [new("shared", "系统共享")];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AvailableFormats))] private AudioOutputMode selectedMode = new("shared", "系统共享");
    [ObservableProperty] private int outputChannels = 2;
    [ObservableProperty] private int sampleRate = 48000;
    [ObservableProperty] private int bufferFrames = 1024;
    [ObservableProperty] private int asioChannelStart = 1;
    [ObservableProperty] private OutputFormatChoice selectedFormat = new("pcm", "PCM（含 DSD 解码）");
    [ObservableProperty] private int dsdTransportRate = 176400;
    [ObservableProperty] private bool isAsio;
    [ObservableProperty] private bool isDsd;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(AvailableFormats))] private bool canUseDsd;
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isApplying;
    [ObservableProperty] private double playbackRate = 1;
    [ObservableProperty] private bool equalizerEnabled;
    [ObservableProperty] private bool crossfeedEnabled;
    [ObservableProperty] private string message = "";
    [ObservableProperty] private string dsdCapabilities = "DSD 支持须以驱动探测结果为准。";
    private int requestVersion;
    private int refreshVersion;
    private CancellationTokenSource? deviceQuery;
    private bool updatingModes;
    public bool ShowKernelStreaming { get; set; }
    public IReadOnlyList<OutputFormatChoice> Formats { get; } = [new("pcm", "PCM（含 DSD 解码）"), new("dop24le", "DoP / SDM 输出"), new("dsd-native-raw", "Native DSD / SDM 输出")];
    public IReadOnlyList<OutputFormatChoice> AvailableFormats => Formats.Where(format => format.Key == "pcm" || CanUseDsd
        && (format.Key == "dop24le" && SelectedMode?.Key is "exclusive" or "asio" || format.Key == "dsd-native-raw" && IsAsio)).ToArray();
    public IReadOnlyList<int> Rates { get; } = [44100, 48000, 88200, 96000, 176400, 192000, 352800, 384000, 705600, 768000];
    public IReadOnlyList<int> DsdRates { get; } = [176400, 192000, 352800, 384000, 705600, 768000, 1411200, 1536000];

    partial void OnSelectedModeChanged(AudioOutputMode value)
    {
        // ComboBox may clear selection briefly while its device/mode list changes.
        if (value is null) return;
        IsAsio = value.Key == "asio";
        if (!IsAsio) OutputChannels = 2;
        if (!updatingModes)
        {
            RefreshCommand.Cancel(); refreshVersion++;
            if (!AvailableFormats.Contains(SelectedFormat)) SelectedFormat = Formats[0];
            _ = LoadDevicesAsync();
        }
    }
    partial void OnSelectedFormatChanged(OutputFormatChoice value)
    { IsDsd = value is not null && value.Key != "pcm"; if (IsDsd) OutputChannels = 2; }
    partial void OnSelectedDeviceChanged(AudioOutputDevice? value) => DsdCapabilities = "DSD 支持须以驱动探测结果为准。";

    [RelayCommand(IncludeCancelCommand = true)] private async Task RefreshAsync(CancellationToken token)
    {
        var version = ++refreshVersion;
        IsRefreshing = true;
        try
        {
            var modes = await session.GetOutputModesAsync(token);
            if (version != refreshVersion || token.IsCancellationRequested) return;
            var current = session.RequestedOutput;
            var mode = current is { } settings && settings.TryGetProperty("outputMode", out var outputMode) ? outputMode.GetString() : SelectedMode?.Key ?? "shared";
            updatingModes = true;
            try { Modes = ShowKernelStreaming ? modes : modes.Where(item => item.Key != "ks").ToArray(); CanUseDsd = session.GeneratedDsdAllowed; SelectedMode = Modes.FirstOrDefault(x => x.Key == mode) ?? Modes[0]; }
            finally { updatingModes = false; }
            if (current is { } config)
            {
                OutputChannels = Int(config, "channels", 2); SampleRate = Int(config, "sampleRate", 48000);
                BufferFrames = Int(config, "bufferSize", 1024); AsioChannelStart = Int(config, "asioOutputChannelStart", 0) + 1;
                if (config.TryGetProperty("processing", out var processing) && processing.TryGetProperty("outputFormat", out var format))
                {
                    SelectedFormat = AvailableFormats.FirstOrDefault(x => x.Key == format.GetString()) ?? Formats[0];
                    if (SelectedFormat.Key != "pcm") { DsdTransportRate = SelectedFormat.Key == "dsd-native-raw" ? SampleRate / 16 : SampleRate; SampleRate = 48000; }
                }
            }
            if (SelectedFormat is null || !AvailableFormats.Contains(SelectedFormat)) SelectedFormat = Formats[0];
            await LoadDevicesAsync(token, duringRefresh: true);
            if (version != refreshVersion || token.IsCancellationRequested) return;
            var eq = await session.DspAsync("eq.getState", Json(new JsonArray()), token: token);
            var crossfeed = await session.DspAsync("crossfeed.getState", Json(new JsonArray()), token: token);
            var retainedRate = await session.GetRetainedDspRequestAsync("playbackRate.configure", token);
            if (version != refreshVersion || token.IsCancellationRequested) return;
            EqualizerEnabled = eq.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.True;
            CrossfeedEnabled = crossfeed.TryGetProperty("enabled", out var crossEnabled) && crossEnabled.ValueKind == JsonValueKind.True;
            var rateConfig = retainedRate is { ValueKind: JsonValueKind.Array } array && array.GetArrayLength() > 0 ? array[0] : default;
            PlaybackRate = rateConfig.ValueKind == JsonValueKind.Object && rateConfig.TryGetProperty("rate", out var rate) && rate.TryGetDouble(out var number) ? Math.Clamp(number, 0.5, 2) : 1;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { if (version == refreshVersion) Message = "无法读取输出状态，请刷新重试。"; }
        finally { if (version == refreshVersion) IsRefreshing = false; }
    }

    private async Task LoadDevicesAsync(CancellationToken cancellation = default, bool duringRefresh = false)
    {
        deviceQuery?.Cancel();
        using var query = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deviceQuery = query;
        var version = ++requestVersion;
        var mode = SelectedMode?.Key ?? "shared";
        var selectedId = SelectedDevice?.Mode == mode ? SelectedDevice.Id : null;
        Devices = []; SelectedDevice = null; IsRefreshing = true;
        try
        {
            var found = await session.GetOutputDevicesAsync(mode, query.Token);
            if (version != requestVersion || SelectedMode?.Key != mode || query.IsCancellationRequested) return;
            Devices = mode == "shared" ? new[] { new AudioOutputDevice("", "系统默认输出", mode, -1, true, 0, []) }.Concat(found).ToArray() : found;
            if (session.RequestedOutput is { } saved && saved.TryGetProperty("outputMode", out var savedMode) && savedMode.GetString() == mode
                && saved.TryGetProperty("deviceId", out var id)) selectedId ??= id.GetString();
            SelectedDevice = Devices.FirstOrDefault(x => x.Id == selectedId) ?? Devices.FirstOrDefault(x => x.IsDefault) ?? Devices.FirstOrDefault();
            Message = Devices.Count == 0 ? "未发现此模式的设备。请连接设备、安装对应驱动后刷新。" : "设备已读取，播放时会确认实际输出。";
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested) { }
        catch (Exception) { if (version == requestVersion) Message = "驱动枚举失败或超时，请刷新重试。"; }
        finally { if (ReferenceEquals(deviceQuery, query)) deviceQuery = null; if (version == requestVersion && !duringRefresh) IsRefreshing = false; }
    }

    public void CancelPendingRead()
    { refreshVersion++; requestVersion++; RefreshCommand.Cancel(); deviceQuery?.Cancel(); IsRefreshing = false; }

    [RelayCommand] private async Task ResetAsync()
    {
        CancelPendingRead(); updatingModes = true;
        try { SelectedMode = Modes.FirstOrDefault(mode => mode.Key == "shared") ?? new("shared", "系统共享"); }
        finally { updatingModes = false; }
        var device = new AudioOutputDevice("", "系统默认输出", "shared", -1, true, 0, []);
        Devices = [device]; SelectedDevice = device;
        SampleRate = 48000; BufferFrames = 1024; OutputChannels = 2; AsioChannelStart = 1; SelectedFormat = Formats[0];
        EqualizerEnabled = false; CrossfeedEnabled = false; PlaybackRate = 1;
        await ApplyAsync();
    }

    [RelayCommand] private async Task ProbeDsdAsync()
    {
        if (SelectedDevice is null || !IsAsio) return;
        try
        {
            var selected = SelectedDevice;
            var result = await session.ProbeAsioDsdAsync(selected);
            if (SelectedDevice != selected) return;
            DsdCapabilities = $"DoP 传输率：{RatesText(result, "dopTransportSampleRates")}\nNative DSD 位速率：{RatesText(result, "nativeDsdSampleRates")}";
        }
        catch (Exception) { DsdCapabilities = "请先停止播放再探测；驱动占用或设备未连接也会导致失败。"; }
    }

    [RelayCommand] private async Task ApplyAsync()
    {
        if (IsApplying) return;
        if (IsRefreshing || SelectedDevice is null || SelectedMode is null || SelectedFormat is null) { Message = "请先选择当前模式下的输出设备与格式。"; return; }
        var equalizer = EqualizerEnabled; var crossfeed = CrossfeedEnabled; var rate = PlaybackRate;
        IsApplying = true;
        try
        {
            await session.ConfigureOutputAsync(new AudioOutputConfiguration(SelectedMode.Key, SelectedDevice, OutputChannels,
                SampleRate, BufferFrames, AsioChannelStart - 1, SelectedFormat.Key, DsdTransportRate));
            await session.DspAsync("eq.setEnabled", Json(new JsonArray(JsonValue.Create(equalizer))), true);
            await session.DspAsync("crossfeed.setState", Json(new JsonArray(new JsonObject { ["enabled"] = crossfeed })), true);
            await session.SetPlaybackRateAsync(rate);
            Message = session.Snapshot.OperationId > 0 ? "输出已打开，请在信号路径查看实际格式。" : "配置已保存，播放时打开并验证设备。";
        }
        catch (Exception e) { Message = e is ArgumentException or IOException or NotSupportedException ? e.Message : "输出设置失败，请检查设备后重试。"; }
        finally { IsApplying = false; }
    }
    private static int Int(JsonElement value, string name, int fallback) => value.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : fallback;
    private static string RatesText(JsonElement value, string name) => value.TryGetProperty(name, out var rates) && rates.ValueKind == JsonValueKind.Array
        ? rates.GetArrayLength() == 0 ? "不支持 / 未确认" : string.Join(", ", rates.EnumerateArray().Select(x => x.GetInt32().ToString("N0"))) : "未确认";
    private static JsonElement Json(JsonNode node)
    {
        using var buffer = new MemoryStream(); using (var writer = new Utf8JsonWriter(buffer)) node.WriteTo(writer);
        using var document = JsonDocument.Parse(buffer.ToArray()); return document.RootElement.Clone();
    }
}
