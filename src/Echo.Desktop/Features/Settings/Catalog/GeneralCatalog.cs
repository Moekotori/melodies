using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Catalog;

public static partial class SettingsCatalog
{
    private static IReadOnlyList<SettingsSectionDefinition> General() => [
        Section("语言与引导",
            new("ui-scale", "界面缩放", SettingKind.Scale),
            Select("language", "显示语言", "system") with { Help = "选择界面显示语言。" },
            Toggle("language-follow-steam", "跟随 Steam 语言", true, "使用 Steam 客户端的语言偏好。"),
            Action("first-run-wizard", "新手教程", "打开新手教程") with { Help = "重新了解曲库导入和播放器的基本操作。" }),
        Section("窗口与启动",
            Toggle("close-to-tray", "关闭时隐藏到托盘"), Toggle("launch-at-login", "开机自启动"),
            Toggle("ultra-light-mode", "ECHO Ultralight", help: "降低后台资源占用，按需恢复界面。", badge: "推荐开启"),
            Toggle("remember-window-size", "记住窗口尺寸", true, "下次启动使用本次关闭时的窗口大小。"),
            Toggle("memory-protection", "内存智能调度", true, "在内存压力下调整非关键后台工作。"),
            Toggle("sidebar-auto-hide", "隐藏侧栏", help: "鼠标移到左边缘展开侧栏抽屉，也可按 Ctrl+B 打开。"), Toggle("settings-hide-sidebar", "进入设置时隐藏侧栏"),
            Toggle("sidebar-icon-only", "侧栏仅显示图标", help: "折叠导航文字，保留图标入口。"))];

    private static IReadOnlyList<SettingsSectionDefinition> Accessibility() => [
        Section("", new("ui-scale", "界面缩放", SettingKind.Scale),
            Toggle("accessibility-eye-comfort", "护眼模式"), Toggle("accessibility-high-contrast", "高对比度"),
            Toggle("accessibility-focus", "始终显示键盘焦点"), Toggle("accessibility-announcements", "播报播放状态"),
            Toggle("touch-keyboard", "启用屏幕键盘"))];

    private static IReadOnlyList<SettingsSectionDefinition> Appearance() => [
        Section("主题", Choices("theme", "主题", ["浅色", "深色", "跟随系统", "氛围", "亚克力"], "浅色"),
            Toggle("theme-schedule", "定时切换深色模式"), Toggle("invert-mode", "反色模式")),
        Section("主题预设", Action("theme-preset", "主题预设", "经典", "展开主题预设")),
        Section("自定义主题", Action("theme-custom", "自定义当前主题", "经典", "进入编辑")),
        Section("布局", Action("sidebar-layout", "左侧栏", "全部显示", "展开左侧栏布局", "恢复默认")),
        Section("背景与封面", Toggle("artist-wall-artwork", "艺术家封面"), Action("wallpaper", "自定义背景", "选择图片"),
            Choices("album-cover-shape", "专辑封面形状", ["方角", "圆角"])),
        Section("字体与排版", Action("appearance-typography", "主字体", "Outfit", "选择"),
            Action("font-chinese", "中文字体", "Microsoft YaHei", "选择"), Action("font-fallback", "备用字体", "Noto Sans SC", "选择"),
            Toggle("page-text-color", "页面文字颜色", help: "默认跟随主题。"), Action("appearance-reset", "外观默认值", "恢复默认")),
        Section("播放栏与可视化", Toggle("player-top-waveform", "底栏上沿波形"),
            Toggle("player-energy-progress", "高能进度条", help: "开启后用高能热力图替代默认波形；关闭后恢复波形进度条。"),
            Toggle("songs-waveform-scrollbar", "波形滚动条"), Toggle("home-waveform-visualizer", "主页波形图"),
            Toggle("disable-all-animations", "禁用所有动画"))];
}
