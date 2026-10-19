using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Wallpapers;

public sealed record WallpaperProject(int Version, string Kind, string Entry)
{
    public static WallpaperProject Read(string path)
    {
        using var file = File.OpenRead(path);
        if (file.Length > 4096) throw new InvalidDataException("壁纸入口描述过大");
        var project = JsonSerializer.Deserialize(file, WallpaperJson.Default.WallpaperProject)
            ?? throw new InvalidDataException("壁纸入口无效");
        if (project.Version != 1 || project.Kind is not ("scene" or "web")
            || WallpaperPaths.Inside(Path.GetDirectoryName(path)!, project.Entry, false) is null)
            throw new InvalidDataException("壁纸入口不受支持");
        return project;
    }
}

[JsonSerializable(typeof(WallpaperProject))]
[JsonSerializable(typeof(string))]
internal partial class WallpaperJson : JsonSerializerContext;
