using CommunityToolkit.Mvvm.ComponentModel;

namespace Echo.Desktop.Motion;

public enum MotionMode { System, Full, Reduced, Off }
public sealed record MotionChoice(MotionMode Mode, string Label);

// One instance per window; no timer, visual references or playback state.
public sealed partial class MotionPolicy : ObservableObject
{
    public IReadOnlyList<MotionChoice> Choices { get; } = [
        new(MotionMode.System, "跟随系统"), new(MotionMode.Full, "完整动效"),
        new(MotionMode.Reduced, "减少动态效果"), new(MotionMode.Off, "关闭动画")];
    [ObservableProperty] private MotionChoice selectedChoice;
    [ObservableProperty] private bool? systemAllowsAnimation;
    [ObservableProperty] private bool isWindowVisible = true;

    public MotionPolicy() => selectedChoice = Choices[0];
    public bool CanAnimate => IsWindowVisible && SelectedChoice.Mode != MotionMode.Off
        && (SelectedChoice.Mode != MotionMode.System || SystemAllowsAnimation != false);
    public bool UseSpatialMotion => CanAnimate && SelectedChoice.Mode != MotionMode.Reduced;
    public string Status => SelectedChoice.Mode switch
    {
        MotionMode.Off => "状态立即更新，所有过渡已关闭。",
        MotionMode.Reduced => "只保留短暂淡入淡出，关闭位移与缩放。",
        MotionMode.Full => "页面、弹层和控件使用完整过渡。",
        _ => SystemAllowsAnimation switch
        {
            false => "系统已关闭动画，界面将立即更新。",
            true => "正在跟随系统的动画设置。",
            _ => "此平台暂未提供系统动画偏好，可手动选择。"
        }
    };

    partial void OnSelectedChoiceChanged(MotionChoice value) => NotifyPolicy();
    partial void OnSystemAllowsAnimationChanged(bool? value) => NotifyPolicy();
    partial void OnIsWindowVisibleChanged(bool value) => NotifyPolicy();
    private void NotifyPolicy()
    {
        OnPropertyChanged(nameof(CanAnimate));
        OnPropertyChanged(nameof(UseSpatialMotion));
        OnPropertyChanged(nameof(Status));
    }
}
