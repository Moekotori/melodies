using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Catalog;

public static partial class SettingsCatalog
{
    public static IReadOnlyList<SettingsCategoryDefinition> Categories { get; } = [
        new("general", "通用", "语言、窗口与基础行为", "message", "基础", General()),
        new("appearance", "外观", "主题、字体、背景", "palette", "基础", Appearance()),
        new("shortcuts", "操作与快捷键", "快捷键、鼠标手势与通知提示", "keyboard", "基础", Shortcuts()),
        new("accessibility", "无障碍", "视觉、键盘、读屏", "accessibility", "基础", Accessibility()),
        new("playback", "播放", "输出、缓冲与播放控制", "zap", "音频与播放", Playback()),
        new("library", "媒体库", "导入、扫描与清理", "download", "内容与媒体", Library()),
        new("lyrics", "歌词设置", "歌词显示与偏好", "captions", "内容与媒体", Lyrics()),
        new("integrations", "连接与服务", "设备联动、外部服务与网络", "link", "连接与扩展", Integrations()),
        new("steamPresence", "Steam 设置", "集中管理 Rich Presence、成就进度、个人统计与排行榜", "gamepad", "连接与扩展", Steam()),
        new("advancedCustom", "性能与兼容", "资源占用、播放兼容与诊断", "gauge", "系统与支持", Performance()),
        new("dataBackup", "数据与备份", "本地备份、网盘备份与迁移", "database", "系统与支持", Backup()),
        new("about", "帮助与关于", "版本、产品文档与排障支持", "info", "系统与支持", About()),
        new("danger", "危险操作", "恢复、清理与本地数据重置", "trash", "系统与支持", Danger())];

    private static SettingDefinition Toggle(string id, string title, bool value = false, string help = "", string badge = "")
        => new(id, title, Checked: value, Help: help, Badge: badge);
    private static SettingDefinition Choices(string id, string title, string[] options, string value = "", string help = "")
        => new(id, title, SettingKind.Choice, options, value.Length == 0 ? options[0] : value, Help: help);
    private static SettingDefinition Select(string id, string title, string value, params string[] options)
        => new(id, title, SettingKind.Select, options.Length > 0 ? options : [value], value);
    private static SettingDefinition Action(string id, string title, params string[] actions)
        => new(id, title, SettingKind.Action, actions);
    private static SettingsSectionDefinition Section(string title, params SettingDefinition[] rows) => new(title, rows);
}
