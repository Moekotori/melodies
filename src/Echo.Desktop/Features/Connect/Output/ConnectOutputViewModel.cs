using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Connect.Runtime;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Playback;

namespace Echo.Desktop.Features.Connect.Output;

// Local outputs only. A row becomes current after the audio host accepts the configuration.
public sealed partial class ConnectOutputViewModel : ObservableObject, IDisposable
{
    private readonly ConnectRuntime runtime;
    private readonly PlaybackSessionCoordinator? session;
    private readonly FeaturePreferences preferences;
    private readonly Action openSongs;
    private readonly HashSet<string> pinned = new(StringComparer.Ordinal);
    private readonly HashSet<string> hidden = new(StringComparer.Ordinal);
    private readonly List<ConnectDeviceItem> items = [];
    private CancellationTokenSource? scan;
    private int refreshVersion;
    private int scans;
    private int started;
    private int disposed;
    private bool restoring = true;

    public ConnectOutputViewModel(PlayerViewModel? player, FeaturePreferences preferences, ConnectRuntime runtime, Action openSongs)
    {
        Player = player;
        this.preferences = preferences;
        this.runtime = runtime;
        this.openSongs = openSongs;
        session = player?.Session;
        Now.Attach(player);
        foreach (var key in ConnectPreferences.ReadKeys(preferences, "connect.output.pinned")) pinned.Add(key);
        foreach (var key in ConnectPreferences.ReadKeys(preferences, "connect.output.hidden")) hidden.Add(key);
        DevicesExpanded = !ConnectPreferences.Enabled(preferences, "connect.output.collapsed");
        ComputerVolume = ConnectPreferences.Read(preferences, "connect.volume") != "device";
        restoring = false;
        Now.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ConnectNowPlaying.PlayLabel) or nameof(ConnectNowPlaying.CanTransport) or nameof(ConnectNowPlaying.HasTrack))
                PullCast();
        };
    }

    public PlayerViewModel? Player { get; }
    public ConnectNowPlaying Now { get; } = new();
    public ObservableCollection<ConnectDeviceItem> Visible { get; } = [];
    [ObservableProperty] private ConnectDeviceFilter filter = ConnectDeviceFilter.All;
    [ObservableProperty] private string query = "";
    [ObservableProperty] private string message = "";
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isApplying;
    [ObservableProperty] private bool devicesExpanded = true;
    [ObservableProperty] private bool computerVolume = true;
    [ObservableProperty] private bool usesDedicatedDevice;
    [ObservableProperty] private ConnectDeviceItem? pending;
    [ObservableProperty] private string pendingText = "";
    [ObservableProperty] private int allCount;
    [ObservableProperty] private int currentCount;
    [ObservableProperty] private int pinnedCount;
    [ObservableProperty] private int hiddenCount;
    public bool HasPending => Pending is not null;
    public bool DeviceVolume => !ComputerVolume;
    public bool IsFilterAll => Filter == ConnectDeviceFilter.All;
    public bool IsFilterCurrent => Filter == ConnectDeviceFilter.Current;
    public bool IsFilterPinned => Filter == ConnectDeviceFilter.Pinned;
    public bool IsFilterHidden => Filter == ConnectDeviceFilter.Hidden;
    public bool ShowEmpty => !IsRefreshing && Visible.Count == 0;
    public string RefreshLabel => IsRefreshing ? "停止扫描" : "重新扫描";
    public string EmptyTitle => Filter == ConnectDeviceFilter.Hidden ? "没有隐藏的输出" : Query.Trim().Length > 0 ? "没有匹配的输出" : "没有可显示的输出";
    public string EmptyHint => Filter == ConnectDeviceFilter.Hidden ? "隐藏后的设备会出现在这里。" : "换一个名称，或重新扫描。";
    public string ResultText => $"{Visible.Count} / {items.Count}";
    public string SectionLabel => DevicesExpanded ? "收起" : "展开";
    public string DiagnosticsText => string.Join('\n', [Now.Title, Now.Artist, Now.Status, Now.OutputName, Now.Signal, "音量 " + Now.VolumePercent.ToString("0") + "%"]);

    public void EnsureLoaded()
    {
        if (Interlocked.Exchange(ref started, 1) != 0) return;
        Scan();
    }

    [RelayCommand] private void Scan()
    {
        if (disposed != 0) return;
        if (IsRefreshing) { scan?.Cancel(); return; }
        scan?.Cancel();
        scan = new CancellationTokenSource();
        _ = RefreshAsync(scan.Token);
        _ = DiscoverLanAsync(scan.Token);
    }

    [RelayCommand] private void ShowAll() => Filter = ConnectDeviceFilter.All;
    [RelayCommand] private void ShowCurrent() => Filter = ConnectDeviceFilter.Current;
    [RelayCommand] private void ShowPinned() => Filter = ConnectDeviceFilter.Pinned;
    [RelayCommand] private void ShowHidden() => Filter = ConnectDeviceFilter.Hidden;
    [RelayCommand] private void ToggleDevices() => DevicesExpanded = !DevicesExpanded;
    [RelayCommand] private void UseComputerVolume() => ComputerVolume = true;
    [RelayCommand] private void UseDeviceVolume() => ComputerVolume = false;
    [RelayCommand] private void OpenSongs() => openSongs();
    [RelayCommand] private void TogglePlayback()
    {
        if (runtime.Cast.Active) { _ = ToggleCastAsync(); return; }
        if (Now.CanTransport) Player?.ToggleCommand.Execute(null);
    }
    [RelayCommand] private void StopPlayback()
    {
        if (runtime.Cast.Active) { _ = StopCastAsync(); return; }
        if (Now.HasTrack) Player?.StopCommand.Execute(null);
    }
    [RelayCommand] private void CancelPending() => Pending = null;
    [RelayCommand] private void ConfirmPending() { var item = Pending; Pending = null; if (item is not null) _ = ApplyAsync(item); }
    [RelayCommand] private void ReturnLocal()
    {
        if (session is null || IsApplying) return;
        var current = ReadCurrent();
        if (session.RequestedOutput is not null && current.Mode == "shared" && current.Id.Length == 0) { Message = "已经是系统共享输出。"; return; }
        var device = new AudioOutputDevice("", "系统默认输出", "shared", -1, true, 0, []);
        _ = ApplyAsync(new ConnectDeviceItem(device, "系统共享", false, false, RequestUse, TogglePin, ToggleHidden));
    }

    public void Dispose()
    {
        disposed = 1;
        refreshVersion++;
        lanVersion++;
        scan?.Cancel();
        scan?.Dispose();
        Now.Dispose();
    }

    partial void OnFilterChanged(ConnectDeviceFilter value) => Publish();
    partial void OnQueryChanged(string value) => Publish();
    partial void OnIsRefreshingChanged(bool value) { OnPropertyChanged(nameof(RefreshLabel)); OnPropertyChanged(nameof(ShowEmpty)); }
    partial void OnIsApplyingChanged(bool value) => RefreshUse();
    partial void OnPendingChanged(ConnectDeviceItem? value) => OnPropertyChanged(nameof(HasPending));
    partial void OnComputerVolumeChanged(bool value)
    {
        OnPropertyChanged(nameof(DeviceVolume));
        OnPropertyChanged(nameof(VolumeSliderEnabled));
        OnPropertyChanged(nameof(ShowVolumeHint));
        if (!restoring) ConnectPreferences.Write(preferences, "connect.volume", value ? "computer" : "device");
    }
    partial void OnDevicesExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(SectionLabel));
        if (!restoring) ConnectPreferences.Write(preferences, "connect.output.collapsed", "", !value);
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        if (session is null) { Message = "播放服务尚未连接。"; return; }
        var version = ++refreshVersion;
        items.Clear();
        Publish();
        var failures = 0;
        BeginWork();
        try
        {
            var modes = await session.GetOutputModesAsync(token);
            if (version != refreshVersion || disposed != 0) return;
            foreach (var mode in modes.Take(4))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var found = await session.GetOutputDevicesAsync(mode.Key, token);
                    if (version != refreshVersion || disposed != 0) return;
                    if (mode.Key == "shared") Add(new AudioOutputDevice("", "系统默认输出", "shared", -1, true, 0, []), mode.Label);
                    foreach (var device in found.Take(64))
                        if (device.Mode == mode.Key) Add(device, mode.Label);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { failures++; }
                if (version != refreshVersion || disposed != 0) return;
                MarkCurrent();
                Publish();
            }
            if (version != refreshVersion || disposed != 0) return;
            var current = ReadCurrent();
            var missing = current.Id.Length > 0 && !items.Any(item => item.IsCurrent);
            Message = items.Count == 0
                ? failures > 0 ? "输出设备枚举失败，请稍后重新扫描。" : "没有发现可切换的输出设备。"
                : failures > 0 ? "部分输出模式没有枚举成功，已显示可用设备。"
                : missing ? "已保存的输出这次不在列表中。可以改选下面的设备。"
                : "设备已读取。切换后以音频宿主的确认结果为准。";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { if (version == refreshVersion && disposed == 0) Message = "已停止扫描。"; }
        catch (Exception) { if (version == refreshVersion && disposed == 0) Message = "无法读取输出设备，请重新扫描。"; }
        finally { EndWork(); }
    }

    private async Task ApplyAsync(ConnectDeviceItem item)
    {
        if (session is null || IsApplying || disposed != 0) return;
        IsApplying = true;
        Message = "正在切换输出，等待宿主确认…";
        var active = session.Snapshot.Status is PlaybackStatus.Playing or PlaybackStatus.Paused or PlaybackStatus.Loading;
        try
        {
            await session.ConfigureOutputAsync(ConnectOutputRequest.Create(session.RequestedOutput, item));
            if (disposed != 0) return;
            MarkCurrent();
            Publish();
            Message = active && session.Snapshot.OperationId > 0 ? "输出已确认，播放已在新输出上恢复。请在信号路径查看实际格式。"
                : session.Snapshot.OperationId > 0 ? "输出已打开，请在信号路径查看实际格式。"
                : "配置已保存，播放时打开并验证设备。";
        }
        catch (OperationCanceledException) { if (disposed == 0) Message = "切换已取消。"; }
        catch (Exception error)
        {
            if (disposed == 0) Message = error is ArgumentException or IOException or NotSupportedException or InvalidOperationException
                ? error.Message : "输出设置失败，请检查设备后重试。";
        }
        finally { if (disposed == 0) IsApplying = false; }
    }

    private void RequestUse(ConnectDeviceItem item)
    {
        if (IsApplying || item.IsCurrent || session is null) return;
        if (item.Mode is "exclusive" or "asio" or "ks")
        {
            Pending = item;
            PendingText = $"{item.Name}（{item.ModeLabel}）会直接打开驱动。正在播放的曲目会先停止，确认成功后再恢复；失败则回到原来的输出。";
            return;
        }
        _ = ApplyAsync(item);
    }

    private void TogglePin(ConnectDeviceItem item)
    {
        if (!item.CanOrganize) { Message = "这个设备标识无法固定。"; return; }
        if (item.IsPinned) pinned.Remove(item.Key);
        else if (pinned.Count >= 24) { Message = "最多固定 24 个输出。"; return; }
        else pinned.Add(item.Key);
        item.IsPinned = pinned.Contains(item.Key);
        ConnectPreferences.WriteKeys(preferences, "connect.output.pinned", pinned);
        Publish();
    }

    private void ToggleHidden(ConnectDeviceItem item)
    {
        if (!item.CanOrganize) { Message = "这个设备标识无法隐藏。"; return; }
        if (item.IsHidden) hidden.Remove(item.Key);
        else if (hidden.Count >= 24) { Message = "最多隐藏 24 个输出。"; return; }
        else hidden.Add(item.Key);
        item.IsHidden = hidden.Contains(item.Key);
        ConnectPreferences.WriteKeys(preferences, "connect.output.hidden", hidden);
        Publish();
    }

    private void Add(AudioOutputDevice device, string modeLabel)
    {
        var key = ConnectDeviceItem.KeyOf(device.Mode, device.Id);
        if (items.Any(item => item.Key == key)) return;
        items.Add(new ConnectDeviceItem(device, modeLabel, pinned.Contains(key), hidden.Contains(key), RequestUse, TogglePin, ToggleHidden));
    }

    private void MarkCurrent()
    {
        var current = ReadCurrent();
        var key = ConnectDeviceItem.KeyOf(current.Mode, current.Id);
        foreach (var item in items) item.IsCurrent = item.Key == key;
        UsesDedicatedDevice = items.Any(item => item.IsCurrent && !(item.Mode == "shared" && item.Device.Id.Length == 0));
        RefreshUse();
    }

    private void BeginWork()
    {
        if (Interlocked.Increment(ref scans) == 1) IsRefreshing = true;
    }

    private void EndWork()
    {
        if (Interlocked.Decrement(ref scans) == 0 && disposed == 0) IsRefreshing = false;
    }

    private (string Mode, string Id) ReadCurrent()
    {
        if (session?.RequestedOutput is not { ValueKind: JsonValueKind.Object } config) return ("shared", "");
        var mode = config.TryGetProperty("outputMode", out var outputMode) ? outputMode.GetString() ?? "shared" : "shared";
        var id = config.TryGetProperty("deviceId", out var deviceId) ? deviceId.GetString() ?? "" : "";
        return (mode, id);
    }

    private void Publish()
    {
        var visible = ConnectOutputCatalog.Visible(items, Filter, Query);
        Visible.Clear();
        foreach (var item in visible) Visible.Add(item);
        AllCount = items.Count(item => !item.IsHidden);
        CurrentCount = items.Count(item => item.IsCurrent);
        PinnedCount = items.Count(item => item.IsPinned && !item.IsHidden);
        HiddenCount = items.Count(item => item.IsHidden);
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
        OnPropertyChanged(nameof(ResultText));
    }

    private void RefreshUse() { foreach (var item in items) item.RefreshUse(IsApplying); }

    [RelayCommand] private void RestoreHidden()
    {
        hidden.Clear();
        foreach (var item in items) item.IsHidden = false;
        ConnectPreferences.WriteKeys(preferences, "connect.output.hidden", hidden);
        Publish();
    }
}
