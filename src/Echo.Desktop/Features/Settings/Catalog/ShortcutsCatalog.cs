using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Catalog;

public static partial class SettingsCatalog
{
    private static IReadOnlyList<SettingsSectionDefinition> Shortcuts() => [
        Section("功能入口", Toggle("command-palette-titlebar-button", "显示命令终端入口")),
        Section("界面与操作密度", Toggle("track-context-menu-extra-actions", "右键菜单扩展动作"), Toggle("mouse-gesture-track-switch", "鼠标手势切歌")),
        Section("通知、提示与启动", Toggle("notifications-disabled", "免打扰（关闭通知）"), Toggle("upcoming-track-notice", "下一首预告")),
        new("功能按键绑定", ShortcutRows())];

    private static IReadOnlyList<SettingDefinition> ShortcutRows()
    {
        (string Id, string Title, string Key)[] rows = [
            ("playPause", "播放 / 暂停", "Space"), ("previousTrack", "上一首", "Ctrl K"), ("nextTrack", "下一首", "Ctrl J"),
            ("stop", "停止播放", "S"), ("seekBackward", "快退 10 秒", "Left"), ("seekForward", "快进 10 秒", "Right"),
            ("replayCurrentTrack", "重播当前歌曲", "0"), ("toggleShuffle", "切换随机播放", "Ctrl S"), ("cycleRepeatMode", "切换循环模式", "R"),
            ("volumeUp", "音量提高", "Ctrl Up"), ("volumeDown", "音量降低", "Ctrl Down"), ("toggleMute", "静音 / 取消静音", "M"),
            ("speedUp", "播放加速", "Ctrl ="), ("speedDown", "播放减速", "Ctrl −"), ("resetPlaybackSpeed", "重置播放速度", "Ctrl 0"),
            ("toggleCurrentTrackLiked", "收藏当前歌曲", "L"), ("openLiked", "打开我喜欢的", "Ctrl Shift L"),
            ("openPlaybackQueue", "打开 / 关闭播放队列", "Ctrl Q"), ("openSearch", "打开搜索", "Ctrl F"),
            ("locateCurrentTrack", "定位到当前播放歌曲", "Ctrl G"), ("revealCurrentTrackInFolder", "打开当前歌曲所在文件夹", "Ctrl Shift O"),
            ("openAudioSettings", "打开音频设置", "未绑定"), ("toggleEq", "开关均衡器", "E"),
            ("openLyricsSettings", "打开歌词设置", "未绑定"), ("toggleLyrics", "打开 / 关闭歌词", "Ctrl L"),
            ("toggleDesktopLyrics", "打开 / 关闭桌面歌词", "未绑定"), ("toggleDesktopLyricsLock", "锁定 / 解锁歌词", "未绑定"),
            ("openSettings", "打开设置", "Ctrl ,"), ("toggleMiniPlayer", "打开 / 关闭迷你播放器", "未绑定"),
            ("showMainWindow", "显示主窗口", "仅全局"), ("bossKey", "老板键", "未绑定")];
        return rows.Select(row => new SettingDefinition("shortcut-" + row.Id, row.Title, SettingKind.Shortcut,
            [row.Key, "未绑定"], row.Key)).ToArray();
    }
}
