using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Catalog;

public static partial class SettingsCatalog
{
    private static IReadOnlyList<SettingsSectionDefinition> Backup() => [
        Section("数据与备份", Action("data-backup", "自动数据备份", "选择目录", "立即备份", "导入备份", "打开目录"),
            Action("settings-export", "导入 / 导出设置", "导出设置", "导入设置"),
            Action("data-package", "一键导出 / 迁移 ECHO 数据包", "导出 ECHO 数据包", "恢复入口"),
            Action("webdav-backup", "网盘备份 · WebDAV", "配置网盘备份")),
        Section("定时备份", Toggle("automatic-backup-enabled", "启用自动一致性备份"),
            Choices("automatic-backup-hours", "备份间隔（小时）", ["1", "6", "12", "24", "48", "168"], "24"),
            Choices("automatic-backup-retention", "保留自动备份份数", ["3", "5", "10", "20"], "5"))];

    private static IReadOnlyList<SettingsSectionDefinition> Maintenance() => [
        InTab("曲库维护", "曲库数据库", Action("danger-database", "曲库状态", "检查", "创建快照", "打开目录", "导出诊断")),
        InTab("曲库维护", "扫描清理", Action("danger-duplicates", "扫描清理", "扫描")),
        InTab("诊断与兼容", "图形与兼容", Toggle("danger-hardware-acceleration", "硬件加速", true, "排查图形兼容问题。切换后需要完全重启。")),
        InTab("诊断与兼容", "诊断与安全模式", new("steam-status", "Steamworks", SettingKind.Text, Value: "尚未接入"),
            Action("safe-mode", "安全模式", "打开控制台", "导出诊断包"), Action("diagnostics-assistant", "诊断助手", "收集本机状态"),
            Action("diagnostics", "Diagnostics / 崩溃报告", "刷新", "导出 Markdown", "打开报告文件夹"),
            Action("library-performance-diagnostics", "性能诊断", "查看详细状态"), Action("library-lab", "曲库诊断工具", "打开诊断工具"),
            Action("dev-console", "运行日志", "查看运行日志"))];

    private static IReadOnlyList<SettingsSectionDefinition> Danger() => [
        Section("重置与删除", new("danger-clear-cache", "清空曲库缓存", SettingKind.Action, ["清空曲库缓存"], Help: "移除索引和封面缓存，不会删除音乐文件。"),
            Action("danger-reset", "恢复默认设置", "恢复默认设置")),
        Section("不可逆操作", Action("danger-repair", "重建曲库数据库", "重建曲库数据库"),
            Action("danger-delete-database", "删除曲库数据库", "删除曲库数据库"), Action("danger-delete-all", "恢复为首次启动状态", "恢复首次启动状态"))];
}
