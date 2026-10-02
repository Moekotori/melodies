using Echo.Application.Shortcuts;
using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Catalog;

public static partial class SettingsCatalog
{
    // Search definitions share the same action IDs as the working binding editor.
    private static IReadOnlyList<SettingsSectionDefinition> Shortcuts() => [new("功能按键绑定",
        ShortcutCatalog.Actions.Select(action => new SettingDefinition("shortcut-" + action.Id, action.Title,
            SettingKind.Shortcut, [action.LocalKey ?? "未绑定", action.GlobalKey ?? "未绑定"], action.LocalKey ?? "未绑定")).ToArray())];
}
