using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Bridge;
using Echo.Desktop.Features.Connect.Runtime;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    private IBridgeRuntime? connectRuntime;
    [ObservableProperty] private int connectLaunch;
    public string? ConnectWorkspaceRequest { get; private set; }
    public IBridgeRuntime ConnectServices => connectRuntime ?? throw new InvalidOperationException("Bridge 客户端尚未连接。");
    public void AttachConnect(IBridgeRuntime runtime) => connectRuntime = runtime;

    public void OpenConnect(string workspace)
    {
        ConnectWorkspaceRequest = workspace;
        if (ActiveRouteId == "connect") ConnectLaunch++;
        else Navigate("connect");
    }

    public string? TakeWorkspaceRequest()
    {
        var value = ConnectWorkspaceRequest;
        ConnectWorkspaceRequest = null;
        return value;
    }

    public void RestoreConnect()
    {
        var runtime = ConnectServices;
        runtime.Use(FeaturePreferences);
        runtime.Restore();
    }

    public void DisposeConnect()
    {
        connectRuntime?.Dispose();
        connectRuntime = null;
    }
}
