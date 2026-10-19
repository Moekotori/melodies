using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Connect.HqPlayer;
using Echo.Desktop.Features.Connect.Mobile;
using Echo.Desktop.Features.Connect.Output;
using Echo.Desktop.Features.Connect.Receive;
using Echo.Bridge;
using Echo.Desktop.Features.Connect.Runtime;
using Echo.Desktop.Features.Connect.SmartHome;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Connect;

// Workspace selection only. Output changes stay with the playback session.
public sealed partial class ConnectViewModel : ObservableObject, IDisposable
{
    private readonly FeaturePreferences preferences;
    private readonly IBridgeRuntime runtime;

    public ConnectViewModel(ShellViewModel shell)
    {
        preferences = shell.FeaturePreferences;
        _ = RefreshBridgeAsync(shell.ConnectServices);
        runtime = shell.ConnectServices;
        runtime.Use(preferences);
        Output = new(shell.Player, preferences, runtime, () => shell.NavigateCommand.Execute("songs"));
        HqPlayer = new(preferences, runtime, Report);
        Receive = new(preferences, runtime, Report);
        Mobile = new(preferences, runtime, Report);
        SmartHome = new(preferences, runtime);
        Items = ConnectWorkspaceItem.Create();
        Output.PropertyChanged += OnChildChanged;
        Output.Now.PropertyChanged += OnNowChanged;
        HqPlayer.PropertyChanged += OnChildChanged;
        Select(shell.TakeWorkspaceRequest() is { Length: > 0 } requested ? requested : ConnectPreferences.Read(preferences, "connect.workspace"));
        runtime.Changed += OnRuntime;
        ApplyActivity();
    }

    public ConnectOutputViewModel Output { get; }
    public ConnectHqPlayerViewModel HqPlayer { get; }
    public ConnectReceiveViewModel Receive { get; }
    public ConnectMobileViewModel Mobile { get; }
    public ConnectSmartHomeViewModel SmartHome { get; }
    public IReadOnlyList<ConnectWorkspaceItem> Items { get; }
    [ObservableProperty] private ConnectWorkspaceItem selected = null!;
    [ObservableProperty] private string? alert;
    public bool IsOutput => Selected?.Mode.Key == "output";
    public bool IsHqPlayer => Selected?.Mode.Key == "hqplayer";
    public bool IsReceive => Selected?.Mode.Key == "receive";
    public bool IsMobile => Selected?.Mode.Key == "mobile";
    public bool IsSmartHome => Selected?.Mode.Key == "smartHome";

    [RelayCommand] private void SelectWorkspace(ConnectWorkspaceItem? item) => Select(item?.Mode.Key);
    [RelayCommand] private void DismissAlert() => Alert = null;

    public void Select(string? key)
    {
        var match = Items.FirstOrDefault(item => item.Mode.Key == key) ?? Items[0];
        var changed = Selected != match;
        foreach (var item in Items) item.IsSelected = item == match;
        Selected = match;
        if (changed) Alert = null;
        ConnectPreferences.Write(preferences, "connect.workspace", match.Mode.Key);
        NotifyVisibility();
    }

    public void Dispose()
    {
        Output.PropertyChanged -= OnChildChanged;
        Output.Now.PropertyChanged -= OnNowChanged;
        HqPlayer.PropertyChanged -= OnChildChanged;
        runtime.Changed -= OnRuntime;
        Output.Dispose();
        HqPlayer.Dispose();
        Receive.Dispose();
        SmartHome.Dispose();
    }

    private void Report(string? message) => Alert = message;
    private void OnChildChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectOutputViewModel.UsesDedicatedDevice) or nameof(ConnectHqPlayerViewModel.PortReachable))
            ApplyActivity();
    }

    private void OnRuntime() => Dispatcher.UIThread.Post(RefreshRuntime);

    private void RefreshRuntime()
    {
        Output.PullCast();
        Receive.Pull();
        Mobile.Pull();
        HqPlayer.PullTrack();
        SmartHome.Pull();
        ApplyActivity();
    }

    private void ApplyActivity()
    {
        foreach (var item in Items)
            item.HasActivity = item.Mode.Key switch
            {
                "output" => Output.UsesDedicatedDevice || runtime.Cast.Active,
                "hqplayer" => HqPlayer.PortReachable,
                "receive" => runtime.Receiver.Running,
                "mobile" => runtime.Link.Running,
                "smartHome" => runtime.Mqtt.Connected,
                _ => false
            };
    }

    partial void OnSelectedChanged(ConnectWorkspaceItem value) => NotifyVisibility();
    private void NotifyVisibility()
    {
        OnPropertyChanged(nameof(IsOutput));
        OnPropertyChanged(nameof(IsHqPlayer));
        OnPropertyChanged(nameof(IsReceive));
        OnPropertyChanged(nameof(IsMobile));
        OnPropertyChanged(nameof(IsSmartHome));
    }

    private void OnNowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectNowPlaying.Title) or nameof(ConnectNowPlaying.Artist))
            HqPlayer.PullTrack();
    }
    private static async Task RefreshBridgeAsync(IBridgeRuntime runtime)
    { try { await runtime.RefreshAsync(); } catch (Exception) { } }
}
