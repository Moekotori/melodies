using System.Buffers;
using System.Security.Cryptography;

namespace Echo.Infrastructure.Media;

// One streaming import at a time per editor. Never modifies the source or retains media bytes.
public sealed class ImportedWallpaperStore(string directory)
{
    public async Task<string> ImportAsync(string source, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(source) || !WallpaperMediaFile.IsVideo(source) && !WallpaperMediaFile.IsImage(source))
            throw new InvalidDataException("请选择本地图片或视频");
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var max = WallpaperMediaFile.IsVideo(source) ? 2L * 1024 * 1024 * 1024 : 16L * 1024 * 1024;
        if (input.Length <= 0 || input.Length > max) throw new InvalidDataException("图片最大 16 MiB，视频最大 2 GiB");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pending");
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                long total = 0;
                while (true)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, 65536), token).ConfigureAwait(false);
                    if (count == 0) break;
                    total += count;
                    if (total > max) throw new InvalidDataException("背景素材超过大小限制");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
                await output.FlushAsync(token).ConfigureAwait(false);
            }
            var name = Path.GetFileNameWithoutExtension(source);
            if (name.Length > 60) name = name[..60];
            var path = Path.Combine(directory, name + "-" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant() + Path.GetExtension(source).ToLowerInvariant());
            if (File.Exists(path)) return path;
            var files = Directory.EnumerateFiles(directory).Where(p => p != temporary).Take(33).Select(p => new FileInfo(p)).ToArray();
            if (files.Length >= 32 || files.Sum(f => f.Length) + new FileInfo(temporary).Length > 4L * 1024 * 1024 * 1024)
                throw new InvalidDataException("导入背景已达到 32 个或 4 GiB 的上限");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, false);
            return path;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
