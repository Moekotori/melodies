using System.Reflection;
using System.Text.Json;

namespace Echo.Wallpapers;

internal static class WallpaperPages
{
    public static string Resource(string name)
    {
        using var stream = typeof(WallpaperPages).Assembly.GetManifestResourceStream("Echo.Wallpapers.Runtime." + name)
            ?? throw new InvalidDataException("缺少壁纸运行时资源");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    public static string Host(string entry) => "<!doctype html><html><head><meta charset='utf-8'><style>html,body,iframe{margin:0;width:100%;height:100%;border:0;overflow:hidden;background:#000}iframe{position:fixed;inset:0}</style></head><body>"
        + "<iframe id='wallpaper' sandbox='allow-scripts' referrerpolicy='no-referrer' src='project/" + entry + "'></iframe><script src='host.js'></script></body></html>";
    public static string Scene(string entry, string properties) => "<!doctype html><html><head><meta charset='utf-8'><style>html,body,#scene{margin:0;width:100%;height:100%;overflow:hidden;background:#000}#scene{position:fixed;inset:0}</style>"
        + "<script type='application/json' id='echo-scene-config'>{\"sceneFile\":" + Quote(entry) + ",\"userProperties\":" + properties + "}</script>"
        + "<script src='file/scripts/jsclasses/baseclasses.js'></script></head><body><img id='poster' style='display:none' alt=''><canvas id='scene'></canvas><script src='__runtime.js'></script></body></html>";
    public static string Bridge(string properties) => Resource("web-bridge.js").Replace("__ECHO_USER_PROPERTIES__", properties, StringComparison.Ordinal);
    public static string Quote(string value) => JsonSerializer.Serialize(value, WallpaperJson.Default.String).Replace("<", "\\u003c");
    public static string Properties(string root)
    {
        var path = WallpaperPaths.Inside(root, "project.json");
        if (path is null) return "{}";
        using var file = File.OpenRead(path);
        if (file.Length > 2 * 1024 * 1024) throw new InvalidDataException("壁纸属性文件过大");
        using var reader = new StreamReader(file);
        using var document = JsonDocument.Parse(reader.ReadToEnd());
        return document.RootElement.TryGetProperty("general", out var general) && general.ValueKind == JsonValueKind.Object
            && general.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
            ? properties.GetRawText().Replace("<", "\\u003c") : "{}";
    }
    public static string Inject(string html)
    {
        const string script = "<script src='__bridge.js'></script>";
        var head = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        var end = head < 0 ? -1 : html.IndexOf('>', head);
        if (end >= 0) return html.Insert(end + 1, script);
        var doc = html.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase) ? html.IndexOf('>') + 1 : 0;
        return html.Insert(doc, script);
    }
}
