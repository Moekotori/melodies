namespace Echo.Application.Shortcuts;

public sealed record ShortcutAction(string Id, string Title, string Group, string? LocalKey = null,
    string? GlobalKey = null, bool LocalEnabled = false);

public static class ShortcutCatalog
{
    // D:\ECHO src/shared/types/globalShortcuts.ts. Global defaults are disabled/unbound.
    public static IReadOnlyList<ShortcutAction> Actions { get; } = [
        new("playPause", "播放 / 暂停", "播放", "Space", "Ctrl+Space", true),
        new("previousTrack", "上一首", "播放", "Ctrl+K", "Ctrl+K"),
        new("nextTrack", "下一首", "播放", "Ctrl+J", "Ctrl+J"),
        new("stop", "停止播放", "播放", "S"),
        new("seekBackward", "快退 10 秒", "播放", "Left", "Ctrl+Left"),
        new("seekForward", "快进 10 秒", "播放", "Right", "Ctrl+Right"),
        new("replayCurrentTrack", "重播当前歌曲", "播放", "0"),
        new("toggleShuffle", "切换随机播放", "播放", "Ctrl+S"),
        new("cycleRepeatMode", "切换循环模式", "播放", "R"),
        new("volumeUp", "音量提高", "音量与速度", "Ctrl+Up", "Ctrl+Up"),
        new("volumeDown", "音量降低", "音量与速度", "Ctrl+Down", "Ctrl+Down"),
        new("toggleMute", "静音 / 取消静音", "音量与速度", "M"),
        new("speedUp", "播放加速", "音量与速度", "Ctrl+="),
        new("speedDown", "播放减速", "音量与速度", "Ctrl+-"),
        new("resetPlaybackSpeed", "重置播放速度", "音量与速度", "Ctrl+0"),
        new("toggleCurrentTrackLiked", "喜欢当前歌曲", "曲库与界面", "L"),
        new("openLiked", "打开我喜欢的", "曲库与界面", "Ctrl+Shift+L"),
        new("openPlaybackQueue", "打开 / 关闭播放队列", "曲库与界面", "Ctrl+Q", LocalEnabled: true),
        new("openSearch", "打开搜索", "曲库与界面", "Ctrl+F"),
        new("openGlobalSearch", "全局搜索", "曲库与界面", "Ctrl+Shift+F"),
        new("locateCurrentTrack", "定位当前歌曲", "曲库与界面", "Ctrl+G"),
        new("revealCurrentTrackInFolder", "打开歌曲所在文件夹", "曲库与界面", "Ctrl+Shift+O"),
        new("openAudioSettings", "打开音频设置", "设置与歌词"),
        new("toggleEq", "开关均衡器", "设置与歌词", "E"),
        new("openMvSettings", "打开 MV 设置", "设置与歌词"),
        new("openLyricsSettings", "打开歌词设置", "设置与歌词"),
        new("toggleLyrics", "打开 / 关闭歌词", "设置与歌词", "Ctrl+L"),
        new("toggleDesktopLyrics", "打开 / 关闭桌面歌词", "设置与歌词"),
        new("toggleDesktopLyricsLock", "锁定 / 解锁桌面歌词", "设置与歌词"),
        new("openSettings", "打开设置", "窗口", "Ctrl+,"),
        new("toggleMiniPlayer", "打开 / 关闭迷你播放器", "窗口"),
        new("togglePet", "打开 / 关闭桌宠", "窗口"),
        new("showMainWindow", "显示主窗口", "窗口", GlobalKey: "Ctrl+E"),
        new("bossKey", "老板键", "窗口", GlobalKey: "Ctrl+B")
    ];
}
