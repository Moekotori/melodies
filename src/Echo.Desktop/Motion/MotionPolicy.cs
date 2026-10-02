using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Motion;

public enum MotionMode { System, Full, Reduced, Off }
public sealed record MotionChoice(MotionMode Mode, string Label);

// One instance per window; no timer, visual references or playback state.
public sealed partial class MotionPolicy : ObservableObject
{
    public IReadOnlyList<MotionChoice> Choices { get; private set; } = [
        new(MotionMode.System, "跟随系统"), new(MotionMode.Full, "完整动效"),
        new(MotionMode.Reduced, "减少动态效果"), new(MotionMode.Off, "关闭动画")];
    [ObservableProperty] private MotionChoice selectedChoice;
    [ObservableProperty] private bool? systemAllowsAnimation;
    [ObservableProperty] private bool isWindowVisible = true;
    [ObservableProperty] private bool themeAllowsAnimation = true;
    [ObservableProperty] private double themeDurationScale = 1;
    [ObservableProperty] private double themeIntensityScale = 1;
    [ObservableProperty] private bool lightweightMode;
    [ObservableProperty] private int maximumFrameRate = 60;

    private readonly LocalizationService Text;
    public MotionPolicy(LocalizationService? text = null)
    { Text = text ?? new(); selectedChoice = Choices[0]; Text.Changed += RefreshLanguage; RefreshLanguage(); }
    private void RefreshLanguage()
    {
        var mode = SelectedChoice.Mode;
        Choices = new[] { new MotionChoice(MotionMode.System, Text.Translate("跟随系统")), new MotionChoice(MotionMode.Full, Text.Translate("完整动效")),
            new MotionChoice(MotionMode.Reduced, Text.Translate("减少动态效果")), new MotionChoice(MotionMode.Off, Text.Translate("关闭动画")) };
        OnPropertyChanged(nameof(Choices)); SelectedChoice = Choices.First(choice => choice.Mode == mode); OnPropertyChanged(nameof(Status));
    }
    public void ReleaseLocalization() => Text.Changed -= RefreshLanguage;
    public bool CanAnimate => IsWindowVisible && ThemeAllowsAnimation && !LightweightMode && SelectedChoice.Mode != MotionMode.Off
        && (SelectedChoice.Mode != MotionMode.System || SystemAllowsAnimation != false);
    public bool UseSpatialMotion => CanAnimate && SelectedChoice.Mode != MotionMode.Reduced;
    public string Status => Text.Translate(SelectedChoice.Mode switch
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
    });

    partial void OnSelectedChoiceChanged(MotionChoice value) => NotifyPolicy();
    partial void OnSystemAllowsAnimationChanged(bool? value) => NotifyPolicy();
    partial void OnIsWindowVisibleChanged(bool value) => NotifyPolicy();
    partial void OnThemeAllowsAnimationChanged(bool value) => NotifyPolicy();
    public TimeSpan Duration(TimeSpan value)
    {
        if (SelectedChoice.Mode == MotionMode.Reduced) return TimeSpan.FromMilliseconds(Math.Min(80, value.TotalMilliseconds));
        var factor = double.IsFinite(ThemeDurationScale) ? Math.Clamp(ThemeDurationScale, 0.1, 36.364) : 1;
        return TimeSpan.FromMilliseconds(Math.Clamp(value.TotalMilliseconds * factor, 0, 8000));
    }
    public double Travel(double value)
    {
        var factor = double.IsFinite(ThemeIntensityScale) ? Math.Clamp(ThemeIntensityScale, 0, 2) : 1;
        return value * factor;
    }
    partial void OnThemeDurationScaleChanged(double value) => NotifyPolicy();
    partial void OnThemeIntensityScaleChanged(double value) => NotifyPolicy();
    partial void OnLightweightModeChanged(bool value) => NotifyPolicy();
    partial void OnMaximumFrameRateChanged(int value) => NotifyPolicy();
    private void NotifyPolicy()
    {
        OnPropertyChanged(nameof(CanAnimate));
        OnPropertyChanged(nameof(UseSpatialMotion));
        OnPropertyChanged(nameof(Status));
    }
}
