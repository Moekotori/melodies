using System.Text.Json;

namespace Echo.Platform.MacOS;

/// <summary>Five macOS shell switches. Missing or unreadable files keep the defaults.</summary>
public sealed class MacOsAccessoryPreferences
{
    public bool DockTile { get; set; } = true;
    public bool DesktopWidget { get; set; }
    public bool NotchPlayer { get; set; }
    public bool PauseOnOutputDisconnect { get; set; } = true;
    public bool PauseOnLidClose { get; set; } = true;

    public static MacOsAccessoryPreferences Load(string directory)
    {
        var prefs = new MacOsAccessoryPreferences();
        try
        {
            var path = Path.Combine(directory, "macos-accessories.json");
            if (!File.Exists(path)) return prefs;
            var text = File.ReadAllText(path);
            if (text.Length is 0 or > 2048) return prefs;
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            prefs.DockTile = Read(root, "dockTile", prefs.DockTile);
            prefs.DesktopWidget = Read(root, "desktopWidget", prefs.DesktopWidget);
            prefs.NotchPlayer = Read(root, "notchPlayer", prefs.NotchPlayer);
            prefs.PauseOnOutputDisconnect = Read(root, "pauseOnOutputDisconnect", prefs.PauseOnOutputDisconnect);
            prefs.PauseOnLidClose = Read(root, "pauseOnLidClose", prefs.PauseOnLidClose);
        }
        catch (Exception) { return new(); }
        return prefs;
    }

    public void Save(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "macos-accessories.json");
            var temporary = path + ".tmp";
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteBoolean("dockTile", DockTile);
                writer.WriteBoolean("desktopWidget", DesktopWidget);
                writer.WriteBoolean("notchPlayer", NotchPlayer);
                writer.WriteBoolean("pauseOnOutputDisconnect", PauseOnOutputDisconnect);
                writer.WriteBoolean("pauseOnLidClose", PauseOnLidClose);
                writer.WriteEndObject();
            }
            File.WriteAllBytes(temporary, stream.ToArray());
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception) { }
    }

    private static bool Read(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : fallback;
}
