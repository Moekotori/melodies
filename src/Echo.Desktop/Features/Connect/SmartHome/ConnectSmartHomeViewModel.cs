using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Bridge;
using Echo.Desktop.Features.Connect.Runtime;
using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Features.Connect.SmartHome;

public sealed partial class ConnectSmartHomeViewModel : ObservableObject, IDisposable
{
    private readonly FeaturePreferences preferences;
    private readonly IBridgeRuntime runtime;
    private CancellationTokenSource? test;
    private int testVersion;
    private bool restoring = true;

    public ConnectSmartHomeViewModel(FeaturePreferences preferences, IBridgeRuntime runtime)
    {
        this.preferences = preferences;
        this.runtime = runtime;
        var lines = ConnectPreferences.Read(preferences, "connect.mqtt").Split('\n');
        Host = lines.Length > 0 && ConnectAddress.IsHost(lines[0]) ? lines[0] : "127.0.0.1";
        PortText = lines.Length > 1 && ConnectAddress.TryPort(lines[1], out _) ? lines[1] : "1883";
        Topic = lines.Length > 2 && ConnectAddress.IsTopic(lines[2]) ? lines[2] : "echo/playback";
        Username = lines.Length > 3 ? lines[3] : "";
        restoring = false;
        Pull();
    }

    [ObservableProperty] private string host = "127.0.0.1";
    [ObservableProperty] private string portText = "1883";
    [ObservableProperty] private string topic = "echo/playback";
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool portReachable;
    [ObservableProperty] private string message = "密码只留在这次连接里。播放状态在曲目、状态或音量变化时发布，不跟随进度。";
    public string StateLabel => runtime.Mqtt.Connected ? "正在发布" : "未连接";
    public string ActionLabel => runtime.Mqtt.Connected ? "断开" : "连接并发布";
    public string StateTopic => Topic.Trim() + "/state";
    public string CommandTopic => Topic.Trim() + "/command";
    public string LastCommand => runtime.Mqtt.LastCommand.Length == 0 ? "还没有收到命令" : runtime.Mqtt.LastCommand;

    public void Pull()
    {
        PortReachable = runtime.Mqtt.Connected;
        if (runtime.Mqtt.Error.Length > 0) Message = runtime.Mqtt.Error;
        OnPropertyChanged(nameof(StateLabel));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(LastCommand));
        OnPropertyChanged(nameof(StateTopic));
        OnPropertyChanged(nameof(CommandTopic));
    }

    [RelayCommand]
    private async Task TestAsync()
    {
        if (runtime.Mqtt.Connected) { await runtime.Mqtt.DisconnectAsync(); ConnectPreferences.Write(preferences, "connect.mqtt", Saved(), false); Pull(); return; }
        if (!Save()) return;
        test?.Cancel();
        test = new CancellationTokenSource();
        var version = ++testVersion;
        IsBusy = true;
        try
        {
            if (!ConnectAddress.TryPort(PortText, out var port)) return;
            await runtime.Mqtt.ConnectAsync(Host.Trim(), port, Topic.Trim(), Username.Trim(), Password, test.Token);
            if (version != testVersion) return;
            ConnectPreferences.Write(preferences, "connect.mqtt", Saved(), Username.Trim().Length == 0);
            Message = "已连接。状态发布到 " + Topic.Trim() + "/state，命令从 " + Topic.Trim() + "/command 接收。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (ConnectFault.Expected(error)) { if (version == testVersion) Message = error.Message; }
        finally { if (version == testVersion) { IsBusy = false; Pull(); } }
    }

    public bool Save()
    {
        if (restoring) return true;
        var host = Host.Trim();
        var topic = Topic.Trim();
        var username = Username.Trim();
        if (!ConnectAddress.IsHost(host) || !ConnectAddress.TryPort(PortText.Trim(), out var port) || !ConnectAddress.IsTopic(topic) || username.Length > 64 || username.Any(char.IsControl))
        {
            Message = "请输入主机、1–65535 端口，以及只含字母、数字、/、_、- 的主题前缀。";
            return false;
        }
        ConnectPreferences.Write(preferences, "connect.mqtt", host + "\n" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + topic + "\n" + username, Username.Length == 0 && runtime.Mqtt.Connected);
        return true;
    }

    public void Dispose() { testVersion++; test?.Cancel(); test?.Dispose(); }

    partial void OnHostChanged(string value) { if (!restoring) PortReachable = false; }
    partial void OnPortTextChanged(string value) { if (!restoring) PortReachable = false; }
    partial void OnTopicChanged(string value)
    {
        OnPropertyChanged(nameof(StateTopic));
        OnPropertyChanged(nameof(CommandTopic));
    }

    private string Saved()
    {
        ConnectAddress.TryPort(PortText.Trim(), out var port);
        return Host.Trim() + "\n" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n" + Topic.Trim() + "\n" + Username.Trim();
    }
}
