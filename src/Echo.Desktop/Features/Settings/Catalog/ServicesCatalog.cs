using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Catalog;

public static partial class SettingsCatalog
{
    private static IReadOnlyList<SettingsSectionDefinition> Integrations() => [
        InTab("状态与联动", "状态展示与直播输出", Toggle("discord-presence", "Discord 状态"), Toggle("obs-browser-source", "浏览器源输出"), Toggle("stage-api", "Stage API"),
            Action("broadcast-links", "直播输出地址", "复制浏览器源地址", "复制状态接口地址", "查看状态")),
        InTab("状态与联动", "Windows 媒体与任务栏", Action("smtc", "Windows 媒体控件", "重启 SMTC"), Toggle("smtc-lyrics", "SMTC 歌词显示"),
            Toggle("taskbar-mini-player", "任务栏迷你播放器", true), Action("taskbar-mini-player-advanced", "高级设置", "展开"),
            Toggle("taskbar-progress-enabled", "显示任务栏播放进度", true),
            Action("taskbar-playback", "任务栏播放进度", "刷新状态")),
        InTab("状态与联动", "移动设备联动", Action("connect", "Echo Link", "连接设备")),
        InTab("状态与联动", "OBS WebSocket", Action("obs-websocket", "OBS WebSocket", "配置连接")),
        InTab("状态与联动", "Bitfocus Companion", Action("companion", "Bitfocus Companion", "配置连接")),
        InTab("听歌记录", "Last.fm 记录", Toggle("lastfm", "Last.fm"), Action("lastfm-connection", "Last.fm 连接", "连接 Last.fm", "刷新状态"),
            Toggle("lastfm-now-playing", "Last.fm Now Playing", true), Toggle("lastfm-scrobbling", "Last.fm Scrobbling", true)),
        InTab("听歌记录", "ListenBrainz 记录", Toggle("listenbrainz", "ListenBrainz"), Action("listenbrainz-connection", "用户令牌", "配置令牌")),
        InTab("网络与隐私", "网络代理", Select("network-proxy", "网络代理", "关闭", "关闭", "系统代理", "手动代理")),
        InTab("网络与隐私", "网络与隐私", Toggle("disable-network-access", "禁用网络访问"))];

    private static IReadOnlyList<SettingsSectionDefinition> Steam() => [
        Section("动态状态", Choices("steam-presence", "Steam 动态状态", ["关闭", "基础", "详细"])),
        Section("成就与社区", new("steam-achievement-progress", "成就进度", SettingKind.Text, Value: "尚未接入 Steamworks"),
            Toggle("steam-extended-stats", "扩展个人统计"), Toggle("steam-cloud", "便携设置云同步"), Toggle("steam-leaderboards", "聆听排行榜"))];

    private static IReadOnlyList<SettingsSectionDefinition> Lyrics() => [
        Section("歌词显示", Toggle("lyrics-enabled", "歌词显示", true),
            Toggle("network-lyrics", "网络歌词匹配", true, "本地或服务器歌词缺失时，向 AMLL、网易云、QQ 音乐和 LRCLIB 查询歌名、歌手、专辑与时长；不发送文件路径。可随时关闭。")),
        Section("文字与样式", Toggle("lyrics-hide-song-info", "隐藏歌曲信息"), Toggle("lyrics-romanization", "显示罗马音"),
            Toggle("lyrics-japanese-kana", "日文歌词优先显示假名"), Toggle("lyrics-translation", "显示中文翻译", true),
            Toggle("lyrics-current-translation", "仅当前行显示译文"), Select("lyrics-page-style", "歌词样式", "经典"),
            Select("lyrics-font", "歌词字体", "跟随样式"),
            new("lyrics-font-size", "歌词字号", SettingKind.Slider, Value: "40"),
            new("lyrics-secondary-size", "翻译与罗马音字号", SettingKind.Slider, Value: "22"),
            new("lyrics-line-height", "歌词行距", SettingKind.Slider, Value: "110"), Select("lyrics-max-characters", "每行最多字数", "自动", "自动", "12", "18", "24", "32"),
            new("lyrics-context-opacity", "其他歌词透明度", SettingKind.Slider, Value: "49"), Toggle("lyrics-context-blur", "模糊其他歌词")),
        Section("逐字与同步", Toggle("lyrics-word-highlight", "逐字高亮", true), Select("lyrics-highlight-style", "高亮样式", "sweep"), Toggle("lyrics-context", "显示上下文", true)),
        Section("桌面歌词", Toggle("desktop-lyrics", "桌面歌词"), Action("desktop-lyrics-position", "桌面歌词位置", "重置位置"))];
}
