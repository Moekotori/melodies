using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Connect.Runtime;

namespace Echo.Desktop.Features.Connect.Output;

public sealed partial class ConnectOutputViewModel
{
    private int lanVersion;
    public ObservableCollection<ConnectLanDevice> LanDevices { get; } = [];
    public bool HasLanDevices => LanDevices.Count > 0;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string lanStatus = "扫描时会查找 DLNA 渲染器。";
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool casting;
    public bool CanTransport => Now.CanTransport || Casting;
    public bool CanStop => Now.HasTrack || Casting;
    public bool VolumeSliderEnabled => ComputerVolume || Casting;
    public bool ShowVolumeHint => !ComputerVolume;
    public string TransportLabel => Casting ? runtime.Cast.Paused ? "播放" : "暂停" : Now.PlayLabel;
    public string PlaybackNote => Casting ? "当前曲目已交给 " + runtime.Cast.DeviceName + "，本机播放已暂停。" : "底部播放器仍控制本机 Audio Core。这里的切换要等音频宿主确认。";
    public string VolumeHint => Casting ? "松手后把音量发给正在投送的设备。" : "还没有投送到设备，设备音量不可用。";
    public string CastButton => "停止投送";

    public void PullCast()
    {
        Casting = runtime.Cast.Active;
        foreach (var device in LanDevices)
        {
            device.IsActive = Casting && device.Key.Length > 0 && device.Key == runtime.Cast.TargetKey;
            if (device.CanCast) device.ActionLabel = device.IsActive ? "停止投送" : "投送当前曲目";
        }
        OnPropertyChanged(nameof(CanTransport));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(TransportLabel));
        OnPropertyChanged(nameof(PlaybackNote));
        OnPropertyChanged(nameof(VolumeSliderEnabled));
        OnPropertyChanged(nameof(VolumeHint));
    }

    public async Task CommitCastVolumeAsync(double percent)
    {
        if (!runtime.Cast.Active) { Message = "还没有投送到设备。"; return; }
        try
        {
            await runtime.Cast.SetVolumeAsync((int)Math.Clamp(Math.Round(percent), 0, 100));
            Message = "设备已接受音量。";
        }
        catch (Exception error) when (ConnectFault.Expected(error)) { Message = error.Message; }
    }

    private async Task DiscoverLanAsync(CancellationToken token)
    {
        var version = ++lanVersion;
        BeginWork();
        LanStatus = "正在查找 DLNA 渲染器。";
        try
        {
            var renderers = await DlnaDiscovery.SearchAsync(token);
            var airplay = await MdnsBrowse.AirPlayNamesAsync(token);
            if (version != lanVersion || disposed != 0) return;
            await Dispatcher.UIThread.InvokeAsync(() => FillLan(renderers, airplay));
        }
        catch (OperationCanceledException) { if (version == lanVersion && disposed == 0) LanStatus = "已停止查找局域网设备。"; }
        catch (Exception error) when (ConnectFault.Expected(error)) { if (version == lanVersion && disposed == 0) LanStatus = error.Message; }
        finally { EndWork(); }
    }

    private void FillLan(IReadOnlyList<DlnaRenderer> renderers, IReadOnlyList<string> airplay)
    {
        LanDevices.Clear();
        foreach (var device in renderers)
            LanDevices.Add(new ConnectLanDevice(device.AvTransport.ControlUrl, "DLNA", device.Name, device.Model.Length == 0 ? device.Address : device.Model + " · " + device.Address, true, new RelayCommand(() => _ = UseLanAsync(device))));
        foreach (var name in airplay)
            LanDevices.Add(new ConnectLanDevice("", "AirPlay", name, "发送协议还不能同步封面和进度，所以不能投送。", false, new RelayCommand(() => { })));
        LanStatus = renderers.Count == 0
            ? "没有发现 DLNA 渲染器。" + (airplay.Count == 0 ? "" : " 附近有 AirPlay 设备，但发送协议还不能用。")
            : "发现 " + renderers.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 台 DLNA 渲染器。投送成功后才会显示已连接。";
        OnPropertyChanged(nameof(HasLanDevices));
        PullCast();
    }

    private async Task UseLanAsync(DlnaRenderer device)
    {
        if (runtime.Cast.Active && runtime.Cast.TargetKey == device.AvTransport.ControlUrl) await StopCastAsync();
        else await CastAsync(device);
    }

    private async Task ToggleCastAsync()
    {
        try
        {
            await runtime.Cast.ToggleAsync();
            PullCast();
        }
        catch (Exception error) when (ConnectFault.Expected(error)) { Message = error.Message; }
    }

    private async Task CastAsync(DlnaRenderer device)
    {
        Message = "正在把当前曲目交给 " + device.Name + "…";
        try
        {
            await runtime.Cast.CastAsync(device, CancellationToken.None);
            PullCast();
            Message = device.Name + " 已接受播放。";
        }
        catch (Exception error) when (ConnectFault.Expected(error)) { Message = error.Message; }
    }

    [RelayCommand]
    private async Task StopCastAsync()
    {
        try
        {
            await runtime.Cast.StopAsync();
            PullCast();
            Message = "已停止投送。";
        }
        catch (Exception error) when (ConnectFault.Expected(error)) { Message = error.Message; }
    }
}

public sealed partial class ConnectLanDevice : ObservableObject
{
    public ConnectLanDevice(string key, string kind, string name, string detail, bool canCast, IRelayCommand castCommand)
    {
        Key = key;
        Kind = kind;
        Name = name;
        Detail = detail;
        CanCast = canCast;
        CastCommand = castCommand;
        ActionLabel = canCast ? "投送当前曲目" : "不能投送";
    }

    public string Key { get; }
    public string Kind { get; }
    public string Name { get; }
    public string Detail { get; }
    public bool CanCast { get; }
    public IRelayCommand CastCommand { get; }
    [ObservableProperty] private bool isActive;
    [ObservableProperty] private string actionLabel = "";
}
