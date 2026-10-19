using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Features.Connect;

public enum ConnectWorkspaceGroup { Cast, Incoming }

public sealed record ConnectWorkspaceMode(string Key, ConnectWorkspaceGroup Group, string Title, string Description, string Hint, string Icon)
{
    public string GroupLabel => Group == ConnectWorkspaceGroup.Cast ? "投送" : "接入";
}

public sealed partial class ConnectWorkspaceItem : ObservableObject
{
    public ConnectWorkspaceItem(ConnectWorkspaceMode mode, bool showGroup)
    {
        Mode = mode;
        ShowGroup = showGroup;
    }

    public ConnectWorkspaceMode Mode { get; }
    public bool ShowGroup { get; }
    public string Title => Mode.Title;
    public string Description => Mode.Description;
    public string Hint => Mode.Hint;
    public string Icon => Mode.Icon;
    public string GroupLabel => Mode.GroupLabel;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool hasActivity;

    public static IReadOnlyList<ConnectWorkspaceItem> Create()
    {
        ConnectWorkspaceMode[] modes =
        [
            new("output", ConnectWorkspaceGroup.Cast, "播放到设备", "切换本机输出，或把当前曲目投到 DLNA。", "本机与 DLNA", "speaker"),
            new("hqplayer", ConnectWorkspaceGroup.Cast, "HQPlayer", "连接控制协议，再把当前曲目送出去。", "外部渲染器", "waves"),
            new("receive", ConnectWorkspaceGroup.Incoming, "从设备接收", "让手机把音乐推到这台 melodies。", "DLNA 渲染器", "download"),
            new("mobile", ConnectWorkspaceGroup.Incoming, "手机遥控", "用配对码让手机控制本机播放。", "配对码", "user"),
            new("smartHome", ConnectWorkspaceGroup.Incoming, "家居联动", "发布播放状态，并接收家居命令。", "MQTT", "zap")
        ];
        return modes.Select((mode, index) => new ConnectWorkspaceItem(mode, index == 0 || modes[index - 1].Group != mode.Group)).ToArray();
    }
}
