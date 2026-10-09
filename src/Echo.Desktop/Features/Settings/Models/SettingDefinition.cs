namespace Echo.Desktop.Features.Settings.Models;

public enum SettingKind { Toggle, Choice, Select, Action, Scale, Slider, Text, Shortcut }
public sealed record SettingDefinition(string Id, string Title, SettingKind Kind = SettingKind.Toggle,
    string[]? Options = null, string Value = "", bool Checked = false, string Help = "", string Badge = "");
public sealed record SettingsSectionDefinition(string Title, IReadOnlyList<SettingDefinition> Rows, string Tab = "");
public sealed record SettingsCategoryDefinition(string Id, string Title, string Description, string Icon,
    string Group, IReadOnlyList<SettingsSectionDefinition> Sections);
