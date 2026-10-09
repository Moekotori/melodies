using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Connect.Runtime;
using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Features.Connect.Receive;

public sealed partial class ConnectReceiveViewModel : ObservableObject, IDisposable
{
    private readonly FeaturePreferences preferences;
    private readonly ConnectRuntime runtime;
    private readonly Action<string?> report;
    private CancellationTokenSource? browse;
    private int browseVersion;

    public ConnectReceiveViewModel(FeaturePreferences preferences, ConnectRuntime runtime, Action<string?> report)
    {
        this.preferences = preferences;
        this.runtime = runtime;
        this.report = report;
        Renderer = Card("接收", "接收来自手机", "从手机投送",
            ["1. 开启后，这台电脑会出现在手机的 DLNA 列表里。", "2. 在手机上选择这里显示的设备名。", "3. 手机按下播放后，本机音频宿主开始出声，这里才显示正在接收。"],
            "只有 SOAP 播放命令被本机接受后，才算接收成功。", "开启接收", ToggleListenCommand);
        AirPlay = Card("AirPlay", "接收来自 iPhone", "从 iPhone 投送",
            ["1. 这里可以查看附近的 AirPlay 设备。", "2. 本机不会广播 AirPlay 接收器。", "3. 没有完成时钟和加密通路之前，开关保持关闭。"],
            "AirPlay 接收需要独立的时钟和加密通路，这一版不打开监听。", "查看附近设备", BrowseAirPlayCommand);
        Pull();
    }

    public ConnectServiceCard Renderer { get; }
    public ConnectServiceCard AirPlay { get; }
    public string AutoStartLabel => ConnectPreferences.Enabled(preferences, "connect.receive") ? "取消自动开启" : "自动开启";
    [ObservableProperty] private bool listening;
    [ObservableProperty] private string listenAddress = "";

    public void Pull()
    {
        var host = runtime.Receiver;
        Renderer.State = host.Running ? "正在监听" : "未开启";
        Renderer.IsLive = host.Running;
        Renderer.IsError = host.Error.Length > 0;
        Renderer.PrimaryLabel = host.Running ? "停止接收" : "开启接收";
        Renderer.Hint = host.Error.Length > 0 ? host.Error : host.Running
            ? (host.Advertising ? "已在局域网广播 DLNA 渲染器。" : "HTTP 已监听，但 SSDP 广播没有成功。")
            : "开启后才会监听。不会提前声称手机已经连上。";
        Renderer.SetFacts(new("状态", host.Running ? "监听中" : "未开启"), new("设备名", host.Name.Length == 0 ? ConnectAddress.Machine() : host.Name),
            new("来源", host.Source));
        Listening = host.Running;
        var address = host.Addresses.Split('、', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        ListenAddress = host.Running && address.Length > 0
            ? "http://" + address + ":" + host.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/description.xml"
            : "";
    }

    [RelayCommand]
    private async Task ToggleListenAsync()
    {
        try
        {
            if (runtime.Receiver.Running)
            {
                await runtime.Receiver.StopAsync();
                ConnectPreferences.Write(preferences, "connect.receive", "", false);
            }
            else
            {
                await runtime.Receiver.StartAsync(preferences);
                ConnectPreferences.Write(preferences, "connect.receive", "", true);
            }
            OnPropertyChanged(nameof(AutoStartLabel));
        }
        catch (Exception error) when (ConnectFault.Expected(error)) { report(error.Message); }
        Pull();
    }

    [RelayCommand]
    private async Task ToggleAutoStartAsync()
    {
        var enable = !ConnectPreferences.Enabled(preferences, "connect.receive");
        ConnectPreferences.Write(preferences, "connect.receive", "", enable);
        OnPropertyChanged(nameof(AutoStartLabel));
        report(enable ? "下次启动会自动监听 DLNA，不会打开 AirPlay。" : "下次启动不会自动监听。");
        if (!enable || runtime.Receiver.Running) return;
        try { await runtime.Receiver.StartAsync(preferences); }
        catch (Exception error) when (ConnectFault.Expected(error)) { report(error.Message); }
        Pull();
    }

    [RelayCommand]
    private async Task StopIncomingAsync()
    {
        try { await runtime.Receiver.StopPlaybackAsync(); Pull(); }
        catch (Exception error) when (ConnectFault.Expected(error)) { report(error.Message); }
    }

    [RelayCommand]
    private async Task BrowseAirPlayAsync()
    {
        browse?.Cancel();
        browse = new CancellationTokenSource();
        var version = ++browseVersion;
        AirPlay.State = "正在查找";
        try
        {
            var names = await MdnsBrowse.AirPlayNamesAsync(browse.Token);
            if (version != browseVersion) return;
            AirPlay.State = "未广播";
            AirPlay.IsLive = false;
            AirPlay.Hint = names.Count == 0
                ? "附近没有发现 AirPlay 设备。本机也没有开始广播。"
                : "发现 " + names.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 台 AirPlay 设备：" + string.Join("、", names) + "。本机接收器仍然关闭。";
            AirPlay.SetFacts(new("状态", "未广播"), new("附近", names.Count == 0 ? "没有发现" : string.Join("、", names.Take(4))), new("本机", "没有监听"));
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() { browseVersion++; browse?.Cancel(); browse?.Dispose(); }

    private static ConnectServiceCard Card(string kicker, string title, string guide, string[] steps, string hint, string action, IRelayCommand command) => new()
    {
        Kicker = kicker, Title = title, GuideTitle = guide, Hint = hint, PrimaryLabel = action, PrimaryCommand = command,
        Steps = steps.Select(step => new ConnectStep(step)).ToArray()
    };
}
