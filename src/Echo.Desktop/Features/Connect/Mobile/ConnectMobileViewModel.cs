using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Connect.Runtime;
using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Features.Connect.Mobile;

public sealed partial class ConnectMobileViewModel : ObservableObject
{
    private readonly FeaturePreferences preferences;
    private readonly ConnectRuntime runtime;
    private readonly Action<string?> report;

    public ConnectMobileViewModel(FeaturePreferences preferences, ConnectRuntime runtime, Action<string?> report)
    {
        this.preferences = preferences;
        this.runtime = runtime;
        this.report = report;
        Card = new ConnectServiceCard
        {
            Kicker = "ECHO Link", Title = "手机遥控", GuideTitle = "配对这台 ECHO",
            PrimaryCommand = ToggleCommand,
            Steps =
            [
                new("1. 开启后，用手机浏览器打开下面的地址。"),
                new("2. 输入这一次的配对码。码三分钟后失效，连续猜错会锁定一分钟。"),
                new("3. 配对成功后，手机上的按钮直接控制本机播放。")
            ]
        };
        Pull();
    }

    public ConnectServiceCard Card { get; }
    [ObservableProperty] private bool showToken;
    public string TokenText => !ShowToken || runtime.Link.Token.Length == 0 ? "已隐藏" : runtime.Link.Token;
    public string Address => runtime.Link.PageUrl.Length == 0 ? "开启后显示" : runtime.Link.PageUrl;
    public string Code => runtime.Link.Code.Length == 0 ? "未开始" : runtime.Link.Code;
    public string CodeDisplay => runtime.Link.Code.Length == 6 ? runtime.Link.Code[..3] + "  " + runtime.Link.Code[3..] : Code;

    public void Pull()
    {
        var host = runtime.Link;
        Card.State = host.Running ? "等待配对" : "未开启";
        Card.IsLive = host.Running;
        Card.IsError = host.Error.Length > 0;
        Card.PrimaryLabel = host.Running ? "关闭配对" : "开启配对";
        Card.Hint = host.Error.Length > 0 ? host.Error : host.Running ? "端口已经打开。配对码过期后可以换一个。" : "关闭时不会监听，也不会生成配对码。";
        Card.SetFacts(new("状态", host.Running ? "监听中" : "未开启"), new("设备名", ConnectAddress.Machine()), new("局域网", host.Running ? host.Addresses : "未监听"));
        OnPropertyChanged(nameof(TokenText));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(Code));
        OnPropertyChanged(nameof(CodeDisplay));
    }

    [RelayCommand]
    private async Task ToggleAsync()
    {
        try
        {
            if (runtime.Link.Running)
            {
                await runtime.Link.StopAsync();
                ConnectPreferences.Write(preferences, "connect.link", "", false);
            }
            else
            {
                await runtime.Link.StartAsync();
                ConnectPreferences.Write(preferences, "connect.link", "", true);
            }
        }
        catch (Exception error) when (ConnectFault.Expected(error)) { report(error.Message); }
        Pull();
    }

    [RelayCommand] private void Rotate() { runtime.Link.Rotate(); Pull(); }
    [RelayCommand] private void Reveal() => ShowToken = !ShowToken;

    partial void OnShowTokenChanged(bool value) => OnPropertyChanged(nameof(TokenText));
}
