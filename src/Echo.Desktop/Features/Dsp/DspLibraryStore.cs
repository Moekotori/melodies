using System.Text.Json.Nodes;

namespace Echo.Desktop.Features.Dsp;

internal static class DspLibraryStore
{
    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "dsp-library.json");

    public static JsonObject Load()
    {
        try
        {
            var info = new FileInfo(FilePath);
            if (!info.Exists || info.Length is 0 or > 512 * 1024) return Empty();
            return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject ?? Empty();
        }
        catch (Exception) { return Empty(); }
    }

    public static void Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, root.ToJsonString());
    }

    private static JsonObject Empty() => new() { ["presets"] = new JsonArray(), ["profiles"] = new JsonArray() };
}
