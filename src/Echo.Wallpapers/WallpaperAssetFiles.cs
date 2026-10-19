namespace Echo.Wallpapers;

internal sealed partial class WallpaperAssetServer
{
    private async Task Serve(Stream stream, string name, string? range, bool head, CancellationToken token)
    {
        switch (name)
        {
            case "host.html":
                var entry = _project.Kind == "scene" ? "__scene.html" : string.Join('/', _project.Entry.Split('/').Select(Uri.EscapeDataString));
                await Text(stream, WallpaperPages.Host(entry), "text/html; charset=utf-8", head, token); return;
            case "host.js": await Text(stream, WallpaperPages.Resource("host.js"), "text/javascript; charset=utf-8", head, token); return;
            case "project/__scene.html" when _project.Kind == "scene":
                await Text(stream, WallpaperPages.Scene(_project.Entry, _properties), "text/html; charset=utf-8", head, token); return;
            case "project/__runtime.js" when _project.Kind == "scene":
                await Text(stream, WallpaperPages.Resource("scene-runtime.js"), "text/javascript; charset=utf-8", head, token); return;
        }
        if (!name.StartsWith("project/", StringComparison.Ordinal)) { await Empty(stream, 404, token); return; }
        name = name[8..];
        if (name.EndsWith("__bridge.js", StringComparison.Ordinal) && _project.Kind == "web")
        { await Text(stream, WallpaperPages.Bridge(_properties), "text/javascript; charset=utf-8", head, token); return; }
        SceneFile? file;
        var video = false;
        if (_project.Kind == "scene")
        {
            video = name.StartsWith("video/", StringComparison.Ordinal);
            if (!video && !name.StartsWith("file/", StringComparison.Ordinal)) { await Empty(stream, 404, token); return; }
            name = name[(video ? 6 : 5)..];
            if (WallpaperPaths.Inside(_root, name, false) is null) { await Empty(stream, 404, token); return; }
            file = _package?.Get(name) ?? Loose(_root, name) ?? (_assets is null ? null : Loose(_assets, name));
            if (video && file is not null) file = await SceneVideoRange.ReadAsync(file, token).ConfigureAwait(false);
        }
        else file = Loose(_root, name);
        if (file is null) { await Empty(stream, 404, token); return; }
        if (_project.Kind == "web" && Path.GetExtension(name).ToLowerInvariant() is ".html" or ".htm")
        {
            if (file.Length > 8 * 1024 * 1024) { await Empty(stream, 413, token); return; }
            using var reader = new StreamReader(file.Path);
            await Text(stream, WallpaperPages.Inject(await reader.ReadToEndAsync(token).ConfigureAwait(false)), "text/html; charset=utf-8", head, token); return;
        }
        await StreamFile(stream, file, video ? "video/mp4" : Mime(name), range, head, token).ConfigureAwait(false);
    }
    private static SceneFile? Loose(string root, string name)
    {
        var path = WallpaperPaths.Inside(root, name);
        return path is null ? null : new(path, 0, new FileInfo(path).Length);
    }
    private static string Mime(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html", ".js" or ".mjs" => "text/javascript", ".json" => "application/json",
        ".css" => "text/css", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp",
        ".gif" => "image/gif", ".svg" => "image/svg+xml", ".mp4" or ".m4v" => "video/mp4", ".webm" => "video/webm",
        ".woff" => "font/woff", ".woff2" => "font/woff2", ".ttf" => "font/ttf", ".wasm" => "application/wasm", _ => "application/octet-stream"
    };
    private async Task StreamFile(Stream stream, SceneFile file, string mime, string? range, bool head, CancellationToken token)
    {
        long first = 0, last = file.Length - 1;
        if (range is not null)
        {
            var parts = range.StartsWith("bytes=", StringComparison.Ordinal) ? range[6..].Split('-') : [];
            if (parts.Length != 2 || !long.TryParse(parts[0], out first) || first < 0 || first >= file.Length
                || parts[1].Length > 0 && (!long.TryParse(parts[1], out last) || last < first))
            { await Header(stream, 416, mime, 0, $"bytes */{file.Length}", token); return; }
            last = Math.Min(last, file.Length - 1);
        }
        var length = Math.Max(0, last - first + 1);
        await Header(stream, range is null ? 200 : 206, mime, length, range is null ? null : $"bytes {first}-{last}/{file.Length}", token);
        if (head) return;
        await using var input = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.RandomAccess);
        input.Position = file.Offset + first;
        var buffer = new byte[65536];
        while (length > 0)
        {
            var count = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(length, buffer.Length)), token).ConfigureAwait(false);
            if (count == 0) throw new EndOfStreamException();
            await stream.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false); length -= count;
        }
    }
}
