using System.Text.Json;

namespace Echo.Infrastructure.Media;

public sealed record WallpaperEngineProject(string Folder, string Title, string Kind, string? MediaPath, string? PreviewPath,
    string? EntryName = null, string? PackagePath = null)
{
    public bool Supported => Kind switch
    {
        "video" => MediaPath is not null && WallpaperMediaFile.IsVideo(MediaPath),
        "image" => MediaPath is not null && WallpaperMediaFile.IsImage(MediaPath),
        "scene" => EntryName is not null && (PackagePath is not null || MediaPath is not null && Path.GetExtension(MediaPath).Equals(".json", StringComparison.OrdinalIgnoreCase)),
        "web" => MediaPath is not null && Path.GetExtension(MediaPath).ToLowerInvariant() is ".html" or ".htm",
        _ => false
    };
    public string TypeLabel => Kind switch
    {
        "video" => Supported ? "视频" : "视频文件缺失或格式不支持",
        "image" => Supported ? "图片" : "图片文件缺失或格式不支持",
        "scene" => Supported ? "场景" : "场景文件缺失", "web" => Supported ? "网页" : "网页入口缺失", _ => "暂不支持"
    };

    public static WallpaperEngineProject Read(string folder)
    {
        folder = Path.GetFullPath(folder);
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("壁纸项目不能为链接目录");
        var projectPath = ResolveFile(folder, "project.json") ?? throw new InvalidDataException("缺少 project.json");
        using var file = File.OpenRead(projectPath);
        if (file.Length is <= 0 or > 2 * 1024 * 1024) throw new InvalidDataException("壁纸项目描述最大 2 MiB");
        using var reader = new StreamReader(file, detectEncodingFromByteOrderMarks: true);
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("壁纸项目描述无效");
        string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
        var rawEntry = Text("file");
        var media = ResolveFile(folder, rawEntry);
        var kind = Text("type")?.ToLowerInvariant();
        if (string.IsNullOrEmpty(kind))
            kind = media is null ? "unknown" : WallpaperMediaFile.IsVideo(media) ? "video" : WallpaperMediaFile.IsImage(media) ? "image"
                : Path.GetExtension(media).ToLowerInvariant() switch { ".pkg" or ".json" => "scene", ".html" or ".htm" => "web", _ => "unknown" };
        var title = Text("title");
        if (string.IsNullOrEmpty(title)) title = Path.GetFileName(folder);
        var entry = rawEntry?.Replace('\\', '/');
        if (kind == "scene" && (string.IsNullOrEmpty(entry) || entry.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase))) entry = "scene.json";
        if (entry is null || entry.Length > 1024 || entry.StartsWith('/') || entry.Contains(':') || entry.Split('/').Any(p => p is "" or "." or "..")) entry = null;
        return new(folder, title.Length > 200 ? title[..200] : title, kind, media, ResolveFile(folder, Text("preview")), entry, ResolveFile(folder, "scene.pkg"));
    }

    // Project metadata cannot select paths outside its folder or follow linked assets.
    private static string? ResolveFile(string folder, string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return null;
        var path = Path.GetFullPath(Path.Combine(folder, relative.Replace('\\', Path.DirectorySeparatorChar)));
        var inside = Path.GetRelativePath(folder, path);
        if (inside == "." || inside == ".." || inside.StartsWith(".." + Path.DirectorySeparatorChar) || Path.IsPathRooted(inside)) return null;
        var current = folder;
        foreach (var part in inside.Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (!Path.Exists(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return null;
        }
        return File.Exists(path) ? path : null;
    }
}
