using System.Buffers.Binary;
using System.Text;

namespace Echo.Wallpapers;

internal sealed record SceneFile(string Path, long Offset, long Length);

// PKGV header only is resident; payloads remain in their source file and are streamed by range.
internal sealed class ScenePackage
{
    private readonly Dictionary<string, SceneFile> _entries = new(StringComparer.OrdinalIgnoreCase);
    public ScenePackage(string path)
    {
        using var file = File.OpenRead(path);
        var header = new byte[(int)Math.Min(file.Length, 8 * 1024 * 1024)];
        file.ReadExactly(header);
        var cursor = 0;
        uint Number()
        {
            if (cursor + 4 > header.Length) throw new InvalidDataException("场景包头损坏");
            var number = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(cursor)); cursor += 4; return number;
        }
        string Text()
        {
            var size = Number();
            if (size > 1024 || cursor + size > header.Length) throw new InvalidDataException("场景包文件名无效");
            var text = Encoding.UTF8.GetString(header, cursor, (int)size); cursor += (int)size; return text;
        }
        var magic = Text();
        if (magic.Length != 8 || !magic.StartsWith("PKGV") || magic[4..].Any(c => c is < '0' or > '9'))
            throw new InvalidDataException("不是 Wallpaper Engine 场景包");
        var count = Number();
        if (count > 20000) throw new InvalidDataException("场景包文件数量过大");
        var entries = new List<(string Name, uint Offset, uint Size)>((int)count);
        for (var i = 0; i < count; i++) entries.Add((Text().Replace('\\', '/'), Number(), Number()));
        foreach (var item in entries)
        {
            if (WallpaperPaths.Inside(Path.GetDirectoryName(path)!, item.Name, false) is null
                || cursor + (long)item.Offset + item.Size > file.Length) throw new InvalidDataException("场景包资源范围无效");
            _entries[item.Name] = new(path, cursor + (long)item.Offset, item.Size);
        }
    }
    public SceneFile? Get(string name) => _entries.GetValueOrDefault(name.Replace('\\', '/'));
}
