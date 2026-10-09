using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Catalog;

public static partial class SettingsCatalog
{
    private static IReadOnlyList<SettingsSectionDefinition> General() => [
        Section("语言与引导",
            Select("language", "显示语言", "system") with { Help = "选择界面显示语言。" },
            Toggle("language-follow-steam", "跟随 Steam 语言", true, "使用 Steam 客户端的语言偏好。"),
            Action("first-run-wizard", "新手教程", "打开新手教程") with { Help = "重新了解曲库导入和播放器的基本操作。" }),
        Section("窗口与启动",
            Toggle("close-to-tray", "关闭时隐藏到托盘"), Toggle("launch-at-login", "开机自启动"),
            Toggle("ultra-light-mode", "ECHO Ultralight", help: "开启后，最小化或隐藏到托盘时卸载主界面和闲置图片；播放、媒体键与浮动窗口继续运行。点击任务栏或托盘恢复界面。关闭后保留主界面。", badge: "推荐开启"),
            Toggle("remember-window-size", "记住窗口尺寸", true, "下次启动使用本次关闭时的窗口大小。"),
            Toggle("sidebar-auto-hide", "隐藏侧栏", help: "鼠标移到左边缘展开侧栏抽屉，也可按 Ctrl+B 打开。"), Toggle("settings-hide-sidebar", "进入设置时隐藏侧栏"),
            Toggle("sidebar-icon-only", "侧栏仅显示图标", help: "折叠导航文字，保留图标入口。")),
        Section("性能与资源占用",
            Toggle("low-spec-mode", "轻量模式"), Select("renderer-frame-rate", "实时视觉帧率", "默认（30 FPS）", "默认（30 FPS）", "60 FPS")),
        Section("版本与文档",
            new("about-version", "版本号", SettingKind.Text, Value: "ECHO Avalonia · 开发版本"),
            Action("about-community", "文档与社区", "产品文档", "哔哩哔哩", "QQ 群", "Discord"),
            Action("about-contributors", "贡献者", "查看贡献者"), Action("about-special-thanks", "特别鸣谢", "查看特别鸣谢"))];

    private static IReadOnlyList<SettingsSectionDefinition> Appearance() => [
        InTab("主题与字体", "主题", Choices("theme", "主题", ["浅色", "深色", "跟随系统", "氛围", "亚克力"], "浅色"),
            Toggle("theme-schedule", "定时切换深色模式"), Toggle("invert-mode", "反色模式"), Toggle("rainbow-mode", "彩虹模式", help: "极光、丝带、色轮、星云、棱镜、潮汐、呼吸与强调色；速度和强度可调。")),
        InTab("主题与字体", "主题预设", Action("theme-preset", "主题预设", "经典", "展开主题预设")),
        InTab("主题与字体", "自定义主题", Action("theme-custom", "自定义当前主题", "经典", "进入编辑")),
        InTab("主题与字体", "字体与排版", Action("appearance-typography", "主字体", "Outfit", "选择"),
            Action("font-chinese", "中文字体", "Microsoft YaHei", "选择"), Action("font-fallback", "备用字体", "Noto Sans SC", "选择"),
            Toggle("page-text-color", "页面文字颜色", help: "默认跟随主题。"), Action("appearance-reset", "外观默认值", "恢复默认")),
        InTab("布局与可视化", "布局", Action("sidebar-layout", "左侧栏", "全部显示", "展开左侧栏布局", "恢复默认")),
        InTab("布局与可视化", "播放栏与可视化", Toggle("player-top-waveform", "底栏上沿波形"),
            Toggle("player-energy-progress", "高能进度条", help: "开启后用高能热力图替代默认波形；关闭后恢复波形进度条。"),
            Toggle("songs-waveform-scrollbar", "波形滚动条"), Toggle("home-waveform-visualizer", "主页波形图"),
            Toggle("disable-all-animations", "禁用所有动画")),
        InTab("背景与封面", "背景与封面", Toggle("artist-wall-artwork", "艺术家封面"), Action("wallpaper", "自定义背景", "选择图片"),
            Choices("album-cover-shape", "专辑封面形状", ["方角", "圆角"])),
        InTab("无障碍", "无障碍", new("ui-scale", "界面缩放", SettingKind.Scale),
            Toggle("accessibility-eye-comfort", "护眼模式"), Toggle("accessibility-high-contrast", "高对比度"),
            Toggle("accessibility-focus", "始终显示键盘焦点"), Toggle("accessibility-announcements", "播报播放状态"),
            Toggle("touch-keyboard", "启用屏幕键盘"))];
}
