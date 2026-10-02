using System.Security.Cryptography;

namespace Echo.Infrastructure.Preferences;

public sealed class ImportedFontStore(string directory)
{
    public static async Task<byte[]> ReadAsync(string path, CancellationToken token)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (file.Length is <= 0 or > 32 * 1024 * 1024) throw new InvalidDataException("字体文件最大 32 MiB");
        var bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes, token).ConfigureAwait(false); return bytes;
    }
    public async Task<string> SaveAsync(byte[] bytes, string extension, CancellationToken token)
    {
        if (extension.ToLowerInvariant() is not (".ttf" or ".otf" or ".ttc")) throw new InvalidDataException("请选择 TTF、OTF 或 TTC 字体文件");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + extension.ToLowerInvariant());
        if (File.Exists(path)) return path;
        var existing = Directory.EnumerateFiles(directory).Take(17).Select(p => new FileInfo(p)).ToArray();
        if (existing.Length >= 16 || existing.Sum(f => f.Length) + bytes.Length > 96 * 1024 * 1024)
            throw new InvalidDataException("导入字体已达到 16 个或 96 MiB 的上限，请使用系统字体");
        var temporary = path + ".pending";
        try { await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false); File.Move(temporary, path, false); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return path;
    }
}
