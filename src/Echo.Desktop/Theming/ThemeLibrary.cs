using System.Text.Json;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Theming;

public sealed partial class ThemeViewModel
{
    [RelayCommand] private void Randomize()
    {
        var accent = Avalonia.Media.Color.FromRgb((byte)Random.Shared.Next(40, 210), (byte)Random.Shared.Next(40, 210), (byte)Random.Shared.Next(40, 210));
        ThemeTone Make(bool dark)
        {
            var tone = Tone(dark); var colors = new Dictionary<string, string>(tone.Colors);
            foreach (var key in new[] { "appBg", "appBg2", "appBg3", "panel", "panelSoft", "titlebar", "sidebar", "player", "field", "row", "chip" })
                colors[key] = ThemeColorMath.Mix(Avalonia.Media.Color.Parse(colors[key]), accent, .08).ToString();
            var active = dark ? ThemeColorMath.Mix(accent, Avalonia.Media.Colors.White, .25) : accent;
            colors["accent"] = active.ToString(); colors["focus"] = active.ToString();
            colors["accentStrong"] = ThemeColorMath.Mix(active, dark ? Avalonia.Media.Colors.White : Avalonia.Media.Colors.Black, .3).ToString();
            colors["onAccent"] = ThemeColorMath.Contrast("#ffffff", colors["accent"]) >= 4.5 ? "#ffffff" : "#14151a";
            colors["rowActive"] = ThemeColorMath.Mix(Avalonia.Media.Color.Parse(colors["row"]), active, .2).ToString();
            return tone with { Colors = colors };
        }
        var pair = new ThemePair(Make(false), Make(true));
        var overrides = new Dictionary<string, ThemePair>(_preferences.Overrides ?? new Dictionary<string, ThemePair>()) { [SelectedPreset.Id] = pair };
        Commit(_preferences with { Light = pair.Light, Dark = pair.Dark, Overrides = overrides, SelectedCustomId = null }, true);
        Name = "随机主题"; Feedback = "已生成配色，可撤销或另存为新主题";
    }
    [RelayCommand] private void SaveCopy()
    {
        if (Library.Count >= 16) { Feedback = "最多保存 16 个自定义主题，请先删除一个"; return; }
        var theme = new CustomTheme(Guid.NewGuid().ToString("N"), string.IsNullOrWhiteSpace(Name) ? "我的主题" : Name.Trim()[..Math.Min(Name.Trim().Length, 60)], SelectedPreset.Id, Tone(false), Tone(true));
        Commit(_preferences with { Library = Library.Append(theme).ToArray(), SelectedCustomId = theme.Id });
        OnPropertyChanged(nameof(Library)); Feedback = "已保存浅色和深色配色";
    }
    [RelayCommand] private void ApplyCustom(CustomTheme? theme)
    {
        if (theme is null) return;
        Commit(_preferences with { Preset = theme.BasePreset, Light = theme.Light, Dark = theme.Dark, SelectedCustomId = theme.Id });
        Restore(_preferences); Name = theme.Name; Feedback = "已应用：" + theme.Name;
    }
    [RelayCommand] private void DeleteCustom(CustomTheme? theme)
    {
        if (theme is null) return;
        Commit(_preferences with { Library = Library.Where(t => t.Id != theme.Id).ToArray(), SelectedCustomId = _preferences.SelectedCustomId == theme.Id ? null : _preferences.SelectedCustomId });
        OnPropertyChanged(nameof(Library)); Feedback = "已删除，可撤销";
    }
    [RelayCommand] private void RenameCustom(CustomTheme? theme)
    {
        if (theme is null || string.IsNullOrWhiteSpace(Name)) return;
        var name = Name.Trim()[..Math.Min(Name.Trim().Length, 60)];
        Commit(_preferences with { Library = Library.Select(t => t.Id == theme.Id ? t with { Name = name } : t).ToArray() });
        OnPropertyChanged(nameof(Library)); Feedback = "已重命名";
    }
    public string ExportJson()
    {
        var theme = new CustomTheme(_preferences.SelectedCustomId ?? Guid.NewGuid().ToString("N"), Name, SelectedPreset.Id, Tone(false), Tone(true));
        // Flat tone properties are the existing ECHO custom-theme v2 interchange format.
        using var stream = new MemoryStream(); using (var writer = new Utf8JsonWriter(stream, new() { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteString("schema", "echo.custom-theme"); writer.WriteNumber("version", 2); writer.WriteString("exportedAt", DateTimeOffset.UtcNow);
            writer.WriteStartObject("theme"); writer.WriteString("id", theme.Id); writer.WriteString("name", theme.Name); writer.WriteString("basePreset", theme.BasePreset);
            writer.WriteString("createdAt", DateTimeOffset.UtcNow); writer.WriteString("updatedAt", DateTimeOffset.UtcNow);
            WriteTone(writer, "light", theme.Light); WriteTone(writer, "dark", theme.Dark); writer.WriteEndObject(); writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
    private static void WriteTone(Utf8JsonWriter writer, string name, ThemeTone tone)
    {
        writer.WriteStartObject(name);
        foreach (var (key, value) in tone.Colors)
        {
            var color = Avalonia.Media.Color.Parse(value);
            var flat = ThemeColorMath.Mix(Avalonia.Media.Color.Parse(tone.Colors["appBg"]), color, color.A / 255d);
            writer.WriteString(key, $"#{flat.R:x2}{flat.G:x2}{flat.B:x2}");
        }
        if (tone.Colors.Any(p => Avalonia.Media.Color.Parse(p.Value).A != 255))
        {
            writer.WriteStartObject("avaloniaColors");
            foreach (var (key, value) in tone.Colors) if (Avalonia.Media.Color.Parse(value).A != 255) writer.WriteString(key, value);
            writer.WriteEndObject();
        }
        foreach (var (key, value) in tone.Numbers) writer.WriteNumber(key, value); writer.WriteBoolean("motionEnabled", tone.MotionEnabled); writer.WriteEndObject();
    }
    public void ImportJson(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 65536) throw new InvalidDataException("主题文件最大 64 KiB");
        using var document = JsonDocument.Parse(json, new() { MaxDepth = 16 });
        var root = document.RootElement;
        if (!root.TryGetProperty("schema", out var schema)) throw new InvalidDataException("这不是 melodies 主题文件");
        var format = schema.GetString(); JsonElement toneRoot; string preset; string name;
        if (format == "echo.custom-theme" && root.GetProperty("version").GetInt32() == 2)
        {
            toneRoot = root.GetProperty("theme");
            if (toneRoot.TryGetProperty("visualMode", out var visual) && visual.GetString() == "stylesheet") throw new InvalidDataException("此主题包含网页样式，需要独立的主题兼容模块");
            preset = toneRoot.GetProperty("basePreset").GetString() ?? "classic"; name = toneRoot.GetProperty("name").GetString() ?? "导入的主题";
        }
        else if (format == "echo.theme-preset" && root.GetProperty("version").GetInt32() == 1)
        { preset = root.GetProperty("preset").GetString() ?? "classic"; toneRoot = root.GetProperty("overrides").GetProperty(preset); name = "导入的主题"; }
        else throw new InvalidDataException("不支持的 melodies 主题版本");
        if (!Presets.Any(p => p.Id == preset)) throw new InvalidDataException("此主题的基础预设尚未迁入");
        if (Library.Count >= 16) throw new InvalidDataException("最多保存 16 个主题，请先删除一个");
        var fallback = ThemeCatalog.Find(preset);
        var theme = new CustomTheme(Guid.NewGuid().ToString("N"), name[..Math.Min(name.Length, 60)], preset,
            ReadTone(toneRoot, "light", fallback.Light), ReadTone(toneRoot, "dark", fallback.Dark));
        Commit(_preferences with { Library = Library.Append(theme).ToArray() }); ApplyCustom(theme); OnPropertyChanged(nameof(Library));
    }
    private static ThemeTone ReadTone(JsonElement parent, string key, ThemeTone fallback)
    {
        if (!parent.TryGetProperty(key, out var tone)) return fallback;
        var colors = new Dictionary<string, string>(); var numbers = new Dictionary<string, double>();
        foreach (var definition in ThemeCatalog.Colors)
        {
            var found = tone.TryGetProperty(definition.Key, out var value);
            if (tone.TryGetProperty("avaloniaColors", out var extra) && extra.TryGetProperty(definition.Key, out var alphaValue)) { found = true; value = alphaValue; }
            if (found)
            { if (!ThemeColorMath.TryHex(value.GetString(), out var hex)) throw new InvalidDataException("无效颜色：" + definition.Label); colors[definition.Key] = hex; }
        }
        foreach (var definition in ThemeCatalog.Numbers)
            if (tone.TryGetProperty(definition.Key, out var value))
            { var number = value.GetDouble(); if (!double.IsFinite(number)) throw new InvalidDataException("无效数值：" + definition.Label); numbers[definition.Key] = number; }
        return ThemeCatalog.Normalize(new(colors, numbers, !tone.TryGetProperty("motionEnabled", out var enabled) || enabled.GetBoolean()), fallback);
    }
}
