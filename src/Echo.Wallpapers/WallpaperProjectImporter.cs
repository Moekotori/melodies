using System.Text.Json;

namespace Echo.Wallpapers;

public static class WallpaperProjectImporter
{
    public static async Task<string> ImportAsync(string source, string destination, string kind, string entry, CancellationToken token)
    {
        if (kind is not ("scene" or "web") || WallpaperPaths.Inside(source, entry, false) is null)
            throw new InvalidDataException("壁纸项目入口无效");
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("不能导入链接目录");
        var files = new List<(string Name, long Size)>();
        var folders = new Stack<string>(); folders.Push(source);
        long total = 0; var visited = 0;
        while (folders.TryPop(out var folder))
        {
            foreach (var item in Directory.EnumerateFileSystemEntries(folder))
            {
                token.ThrowIfCancellationRequested();
                if (++visited > 6000) throw new InvalidDataException("壁纸项目最多 6000 个目录和文件");
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("壁纸项目包含链接文件或目录");
                if ((attributes & FileAttributes.Directory) != 0) { folders.Push(item); continue; }
                var name = Path.GetRelativePath(source, item);
                if (name == WallpaperPaths.ManifestName) throw new InvalidDataException("壁纸项目包含保留入口文件");
                var size = new FileInfo(item).Length; total += size;
                if (files.Count >= 5000 || total > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("壁纸项目最大 5000 个文件或 2 GiB");
                files.Add((name, size));
            }
        }
        Directory.CreateDirectory(destination);
        var existing = Directory.EnumerateDirectories(destination, "*.we-*").Take(17).ToArray();
        if (existing.Length >= 16) throw new InvalidDataException("动态壁纸最多导入 16 个项目");
        var target = Path.Combine(Path.GetFullPath(destination), Guid.NewGuid().ToString("N") + ".we-" + kind);
        Directory.CreateDirectory(target);
        try
        {
            foreach (var item in files)
            {
                token.ThrowIfCancellationRequested();
                var from = WallpaperPaths.Inside(source, item.Name) ?? throw new InvalidDataException("壁纸资源已变化");
                var to = WallpaperPaths.Inside(target, item.Name, false) ?? throw new InvalidDataException("壁纸资源路径无效");
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                await using var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
                if (input.Length != item.Size) throw new InvalidDataException("壁纸资源已变化，请重新导入");
                await using var output = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }
            if (kind == "scene" && WallpaperPaths.Inside(target, "scene.pkg") is { } package)
            {
                if (new ScenePackage(package).Get(entry) is null) throw new InvalidDataException("场景包缺少入口文件");
            }
            else if (WallpaperPaths.Inside(target, entry) is null) throw new InvalidDataException("壁纸缺少入口文件");
            var manifest = Path.Combine(target, WallpaperPaths.ManifestName);
            await using (var output = File.Create(manifest))
                await JsonSerializer.SerializeAsync(output, new WallpaperProject(1, kind, entry.Replace('\\', '/')), WallpaperJson.Default.WallpaperProject, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return manifest;
        }
        catch
        {
            // target is a fresh UUID child of the resolved destination created by this operation.
            if (Path.GetDirectoryName(target) != Path.GetFullPath(destination)) throw;
            Directory.Delete(target, recursive: true);
            throw;
        }
    }
}
