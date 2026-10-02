using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Catalog;

public static partial class SettingsCatalog
{
    private static IReadOnlyList<SettingsSectionDefinition> Playback() => [
        Section("输出与设备", Toggle("auto-output", "自动选择输出", true, badge: "推荐开启"),
            Choices("output-mode", "输出模式", ["安全模式", "共享模式（推荐）", "独占模式", "ASIO"], "共享模式（推荐）"),
            Toggle("wdm-ks", "显示 WDM-KS 设备"), Choices("shared-backend", "共享后端", ["WASAPI Shared", "DirectSound 兼容"]),
            Action("output-device", "输出设备", "刷新并选择设备"),
            Action("audio-recover", "声音异常时", "重启播放"), Toggle("exclusive-fallback", "独占不稳定自动切共享")),
        Section("启动与电源", Toggle("prevent-sleep-while-playing", "播放时保持唤醒"), Toggle("auto-play-on-startup", "启动时自动播放")),
        Section("播放偏好", Choices("speed-mode", "变速模式", ["Nightcore", "Doubletime"], "Doubletime"), Toggle("fixed-volume", "锁定音量"),
            new("transport-fade", "切歌淡入淡出", SettingKind.Slider, Value: "0")),
        Section("睡眠定时与片段循环",
            Choices("sleep-timer-minutes", "默认定时时长（分钟）", ["15", "30", "45", "60", "90", "120"], "30"),
            Choices("sleep-timer-action", "定时结束动作", ["暂停", "停止", "退出应用"]),
            Action("sleep-timer", "睡眠定时", "开始定时", "取消定时"),
            Action("segment-loop", "A–B 片段循环", "清除片段循环")),
        Section("队列衔接与播放顺序", Toggle("automix", "智能过渡", badge: "BETA"), Toggle("gapless-playback", "专辑无缝播放"),
            Choices("shuffle-credibility", "随机播放避重", ["不避重", "避开最近 25 首", "避开最近 100 首"])),
        Section("状态与排障", Action("audio-status", "当前输出状态", "查看详细状态"))];

    private static IReadOnlyList<SettingsSectionDefinition> Performance() => [
        Section("性能与资源占用", Toggle("low-spec-mode", "轻量模式"), Select("renderer-frame-rate", "实时视觉帧率", "默认（30 FPS）", "默认（30 FPS）", "60 FPS")),
        Section("性能与播放", Toggle("low-load-playback", "减轻占用播放"), Toggle("low-load-playback-enhancements", "进一步减轻占用"),
            Toggle("full-frame-presentation", "整帧提交（闪屏排查）"), Toggle("album-wall-virtualization", "全局虚拟化", true),
            Toggle("native-direct-local-playback", "本地文件直读", true), Choices("scan-performance", "扫描性能", ["低占用", "均衡", "高性能", "极速"], "均衡")),
        Section("试验性功能", Toggle("miniaudio-output", "miniaudio WASAPI 输出实验"),
            Action("mini-player", "ECHO 灵动岛", "显示", "重置位置"), Toggle("soxr-fallback", "重采样回退保护", true),
            Toggle("ultra-light-disable-gpu", "超轻模式禁用 Electron GPU", help: "原版兼容设置；Avalonia 不使用 Electron。", badge: "危险选项"),
            Action("library-performance-diagnostics", "性能诊断", "查看详细状态"), Action("library-lab", "曲库诊断工具", "打开诊断工具"),
            Action("dev-console", "运行日志", "查看运行日志")),
        Section("窗口、可视化与图形保护", Toggle("signal-path-control", "显示音频链路"), Toggle("lyrics-mv-graphics-pressure-guard", "歌词图形内存保护", true),
            Toggle("audio-visual-spectrum", "实时频谱分析")),
        Section("数据保护与分析功能", Toggle("sqlite-balanced-durability", "数据库安全写入", true),
            Toggle("fast-startup", "快速启动", true), Toggle("data-protection-disabled", "数据保护快照", true))];

    private static IReadOnlyList<SettingsSectionDefinition> Library() => [
        Section("艺术家资料", Choices("artist-online-info-sources", "艺人信息源", ["百度百科", "Wikipedia"])),
        Section("添加音乐", Toggle("live-library-updates", "自动发现新音乐"), Action("library-folders", "曲库文件夹", "添加文件夹")),
        Section("已隐藏曲目", Action("hidden-tracks", "已隐藏曲目", "查看已隐藏曲目")),
        Section("检查", Action("library-health", "曲库检查", "检查曲库")),
        Section("自动整理与存储", Toggle("auto-backup-playlists", "自动备份歌单", true),
            Action("auto-hide-duplicates", "自动隐藏重复歌曲", "查找重复歌曲"),
            Choices("library-merge-strategy", "专辑和艺人整理方式", ["稳妥整理（推荐）", "更宽松地合并"]),
            Action("reread-metadata", "重新读取歌曲资料", "重新读取全部歌曲", "检查封面与歌词"),
            Action("library-maintenance-cancel", "曲库维护", "取消当前维护"),
            Action("cover-directory", "封面存储位置", "重新统计", "选择文件夹", "恢复默认"),
            Action("detect-bpm", "自动识别BPM", "识别未处理的歌曲"))];
}
