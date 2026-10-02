using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Echo.Infrastructure.Backup;

public sealed record RestoreRequest(string DataDirectory, string StagingDirectory, int ParentPid, long ParentStartedTicks);
public static class BackupRestore
{
    private static readonly string[] Allowed = ["preferences.json", "shortcuts.json", "lyrics-settings.json", "playback.sqlite", "library.sqlite"];
    public static async Task<string> StageAsync(string archivePath, string dataDirectory, CancellationToken token)
    {
        var backup = new LocalBackupService(dataDirectory, null); var manifest = await backup.ValidateAsync(archivePath, token).ConfigureAwait(false);
        var staging = Path.Combine(dataDirectory, "restore-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        await using var source = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        using var zip = new ZipArchive(source, ZipArchiveMode.Read);
        foreach (var file in manifest.Files)
        {
            token.ThrowIfCancellationRequested(); var target = Path.Combine(staging, file.Name);
            await using (var input = zip.GetEntry(file.Name)!.Open())
            await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            await using var hashed = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
            if (hashed.Length != file.Length || Convert.ToHexString(await SHA256.HashDataAsync(hashed, token).ConfigureAwait(false)) != file.Sha256)
                throw new InvalidDataException("恢复暂存文件校验失败。");
        }
        using var parent = Process.GetCurrentProcess();
        var request = new RestoreRequest(Path.GetFullPath(dataDirectory), Path.GetFullPath(staging), parent.Id, parent.StartTime.ToUniversalTime().Ticks);
        var path = Path.Combine(staging, "request.json");
        await File.WriteAllBytesAsync(path, JsonSerializer.SerializeToUtf8Bytes(request, RestoreJsonContext.Default.RestoreRequest), token).ConfigureAwait(false);
        return path;
    }
    public static async Task CompleteAsync(string requestPath, CancellationToken token)
    {
        var expectedRoot = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia"));
        var bytes = await File.ReadAllBytesAsync(requestPath, token).ConfigureAwait(false);
        if (bytes.Length > 8192) throw new InvalidDataException("恢复请求过大。");
        var request = JsonSerializer.Deserialize(bytes, RestoreJsonContext.Default.RestoreRequest) ?? throw new InvalidDataException("恢复请求无效。");
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetFullPath(request.DataDirectory), expectedRoot, comparison)
            || !Path.GetFullPath(request.StagingDirectory).StartsWith(Path.Combine(expectedRoot, "restore-work") + Path.DirectorySeparatorChar, comparison)
            || !string.Equals(Path.GetFullPath(requestPath), Path.Combine(request.StagingDirectory, "request.json"), comparison))
            throw new InvalidDataException("恢复请求不属于本应用数据目录。");
        try
        {
            using var parent = Process.GetProcessById(request.ParentPid);
            if (parent.StartTime.ToUniversalTime().Ticks == request.ParentStartedTicks)
                await parent.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (ArgumentException) { }
        // Prevent opening another writer while source files are moved.
        await using var ownership = new FileStream(Path.Combine(expectedRoot, "library.sqlite.writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var playbackOwnership = new FileStream(Path.Combine(expectedRoot, "playback.sqlite.writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var rollback = Path.Combine(expectedRoot, "restore-rollback", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(rollback);
        var replaced = new List<string>(); var inserted = new List<string>();
        try
        {
            foreach (var name in Allowed)
            {
                var staged = Path.Combine(request.StagingDirectory, name); if (!File.Exists(staged)) continue;
                var target = Path.Combine(expectedRoot, name);
                foreach (var suffix in new[] { "", "-wal", "-shm" })
                {
                    var existing = target + suffix;
                    if (!File.Exists(existing)) continue;
                    File.Move(existing, Path.Combine(rollback, name + suffix)); replaced.Add(name + suffix);
                }
                File.Move(staged, target); inserted.Add(name);
            }
            await File.WriteAllTextAsync(Path.Combine(expectedRoot, "restore-result.txt"), "恢复完成；之前的数据保留在 restore-rollback。", token).ConfigureAwait(false);
        }
        catch
        {
            foreach (var name in inserted) File.Move(Path.Combine(expectedRoot, name), Path.Combine(request.StagingDirectory, name), true);
            foreach (var name in replaced) File.Move(Path.Combine(rollback, name), Path.Combine(expectedRoot, name), true);
            throw;
        }
    }
}
[JsonSerializable(typeof(RestoreRequest))]
internal partial class RestoreJsonContext : JsonSerializerContext;
