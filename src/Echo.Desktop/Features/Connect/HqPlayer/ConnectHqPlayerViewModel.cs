using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Connect.Runtime;
using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Features.Connect.HqPlayer;

public sealed partial class ConnectHqPlayerViewModel : ObservableObject, IDisposable
{
    private readonly FeaturePreferences preferences;
    private readonly ConnectRuntime runtime;
    private readonly Action<string?> report;
    private CancellationTokenSource? test;
    private int testVersion;
    private bool restoring = true;

    public ConnectHqPlayerViewModel(FeaturePreferences preferences, ConnectRuntime runtime, Action<string?> report)
    {
        this.preferences = preferences;
        this.runtime = runtime;
        this.report = report;
        var lines = ConnectPreferences.Read(preferences, "connect.hqplayer").Split('\n');
        IsLocal = lines.Length == 0 || lines[0] != "remote";
        Host = lines.Length > 1 && ConnectAddress.IsHost(lines[1]) ? lines[1] : "127.0.0.1";
        PortText = lines.Length > 2 && ConnectAddress.TryPort(lines[2], out _) ? lines[2] : "4321";
        RememberEnabled = ConnectPreferences.Enabled(preferences, "connect.hqplayer");
        restoring = false;
        PullTrack();
    }

    [ObservableProperty] private bool isLocal = true;
    [ObservableProperty] private string host = "127.0.0.1";
    [ObservableProperty] private string portText = "4321";
    [ObservableProperty] private bool rememberEnabled;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool portReachable;
    [ObservableProperty] private string message = "检测会发送 GetInfo 和 Status。送出曲目前不会启动 HQPlayer。";
    public string StateLabel => PortReachable ? "控制协议已响应" : RememberEnabled ? "尚未检测" : "未启用";
    public string Endpoint => (IsLocal ? "127.0.0.1" : Host.Trim()) + ":" + PortText.Trim();
    public string TrackLine { get; private set; } = "还没有可送出的曲目。";

    public void PullTrack()
    {
        TrackLine = runtime.TrackCaption();
        OnPropertyChanged(nameof(TrackLine));
    }

    [RelayCommand] private void UseLocal() { IsLocal = true; Host = "127.0.0.1"; Save(); }
    [RelayCommand] private void UseRemote() { IsLocal = false; Save(); }

    [RelayCommand] private Task TestAsync() => RunAsync(async (host, port, token) =>
    {
        await runtime.HqPlayer.ProbeAsync(host, port, token);
        PortReachable = true;
        Message = "HQPlayer 已返回控制协议。还没有把曲目送出去。";
    });

    [RelayCommand] private Task HandOffAsync() => RunAsync(async (host, port, token) =>
    {
        await runtime.HqPlayer.HandOffAsync(host, port, token);
        PortReachable = true;
        Message = "HQPlayer 已接受当前曲目。";
    });

    [RelayCommand] private Task PauseAsync() => RunAsync(async (host, port, token) =>
    {
        await runtime.HqPlayer.PauseAsync(host, port, token);
        Message = "已把暂停发给 HQPlayer。";
    });

    [RelayCommand] private Task StopRendererAsync() => RunAsync(async (host, port, token) =>
    {
        await runtime.HqPlayer.StopAsync(host, port, token);
        Message = "已把停止发给 HQPlayer。";
    });

    private async Task RunAsync(Func<string, int, CancellationToken, Task> work)
    {
        if (!Save() || !ConnectAddress.TryPort(PortText, out var port)) return;
        test?.Cancel();
        test = new CancellationTokenSource();
        var version = ++testVersion;
        var token = test.Token;
        IsBusy = true;
        try
        {
            await work(IsLocal ? "127.0.0.1" : Host.Trim(), port, token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (ConnectFault.Expected(error))
        {
            if (version != testVersion) return;
            PortReachable = false;
            Message = error.Message;
            report(error.Message);
        }
        finally { if (version == testVersion) IsBusy = false; }
    }

    public bool Save()
    {
        if (restoring) return true;
        var host = IsLocal ? "127.0.0.1" : Host.Trim();
        if (!ConnectAddress.IsHost(host) || !ConnectAddress.TryPort(PortText.Trim(), out var port))
        {
            Message = "请输入有效的主机和 1–65535 端口。";
            return false;
        }
        ConnectPreferences.Write(preferences, "connect.hqplayer", (IsLocal ? "localDesktop" : "remote") + "\n" + host + "\n" + port.ToString(System.Globalization.CultureInfo.InvariantCulture), RememberEnabled);
        OnPropertyChanged(nameof(Endpoint));
        return true;
    }

    public void Dispose() { testVersion++; test?.Cancel(); test?.Dispose(); }

    partial void OnIsLocalChanged(bool value) => OnPropertyChanged(nameof(Endpoint));
    partial void OnHostChanged(string value) { PortReachable = false; OnPropertyChanged(nameof(Endpoint)); }
    partial void OnPortTextChanged(string value) { PortReachable = false; OnPropertyChanged(nameof(Endpoint)); }
    partial void OnRememberEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(StateLabel));
        if (restoring || Save()) return;
        restoring = true;
        RememberEnabled = !value;
        restoring = false;
    }
    partial void OnPortReachableChanged(bool value) => OnPropertyChanged(nameof(StateLabel));
}
