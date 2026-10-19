using Echo.Contracts.Backup;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Echo.Contracts.Library;

namespace Echo.Infrastructure.Backup;

public sealed class LocalBackupService(string dataDirectory, ILibraryMaintenance? library)
{
    private static readonly string[] Allowed = ["preferences.json", "shortcuts.json", "lyrics-settings.json", "playback.sqlite", "library.sqlite", "podcasts.sqlite", "radio-stations.json", "core-settings.json", "bridge-settings.json"];
    private readonly SemaphoreSlim _exclusive = new(1, 1);
    public async Task CreateAsync(string destination, CancellationToken token)
    {
        if (!await _exclusive.WaitAsync(0, token)) throw new IOException("已有备份或恢复任务正在执行。");
        var workRoot = Path.GetFullPath(Path.Combine(dataDirectory, "backup-work"));
        var staging = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() => Directory.CreateDirectory(staging), token).ConfigureAwait(false);
            var files = new List<BackupFile>();
            foreach (var name in Allowed)
            {
                token.ThrowIfCancellationRequested(); var source = Path.Combine(dataDirectory, name); var target = Path.Combine(staging, name);
                if (name == "library.sqlite" && library is not null) await library.BackupAsync(target, token).ConfigureAwait(false);
                else if (name.EndsWith(".sqlite", StringComparison.Ordinal))
                {
                    if (!File.Exists(source)) continue;
                    await Task.Run(() => SnapshotSqlite(source, target), token).ConfigureAwait(false);
                }
                else
                {
                    if (!File.Exists(source)) continue;
                    await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 8192, FileOptions.Asynchronous);
                    await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);
                    await input.CopyToAsync(output, token).ConfigureAwait(false);
                }
                await using var hashed = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
                files.Add(new(name, hashed.Length, Convert.ToHexString(await SHA256.HashDataAsync(hashed, token).ConfigureAwait(false))));
            }
            var manifest = new BackupManifest(1, DateTimeOffset.UtcNow.ToString("O"), files.ToArray());
            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, BackupJsonContext.Default.BackupManifest);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    foreach (var file in files)
                    {
                        await using var entry = archive.CreateEntry(file.Name, CompressionLevel.Fastest).Open();
                        await using var input = new FileStream(Path.Combine(staging, file.Name), FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
                        await input.CopyToAsync(entry, token).ConfigureAwait(false);
                    }
                    await using var info = archive.CreateEntry("manifest.json").Open(); await info.WriteAsync(manifestBytes, token).ConfigureAwait(false);
                }
                File.Move(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally
        {
            // The exact GUID-owned child is the only directory removed.
            if (Directory.Exists(staging) && string.Equals(Path.GetDirectoryName(Path.GetFullPath(staging)), workRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) Directory.Delete(staging, true);
            _exclusive.Release();
        }
    }
    public Task<BackupManifest> ValidateAsync(string archivePath, CancellationToken token) => Task.Run(async () =>
    {
        await using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        if (archive.Entries.Count > 10) throw new InvalidDataException("备份条目数量超出范围。");
        var info = archive.GetEntry("manifest.json") ?? throw new InvalidDataException("缺少 ECHO 备份清单。");
        if (info.Length > 8192) throw new InvalidDataException("备份清单过大。");
        await using var manifestStream = info.Open();
        var manifest = await JsonSerializer.DeserializeAsync(manifestStream, BackupJsonContext.Default.BackupManifest, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("备份清单无效。");
        if (manifest.Version != 1 || manifest.Files.Length > 9) throw new InvalidDataException("备份版本不受支持。");
        if (manifest.Files.Select(file => file.Name).Distinct(StringComparer.Ordinal).Count() != manifest.Files.Length
            || manifest.Files.Sum(file => file.Length) > 536870912
            || archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
            throw new InvalidDataException("备份包含重复条目或超过解压容量上限。");
        foreach (var file in manifest.Files)
        {
            if (!Allowed.Contains(file.Name) || file.Length is < 0 or > 536870912) throw new InvalidDataException("备份包含非法文件。");
            var entry = archive.GetEntry(file.Name) ?? throw new InvalidDataException("备份文件缺失。");
            if (entry.Length != file.Length) throw new InvalidDataException("备份文件长度不一致。");
            await using var content = entry.Open();
            if (Convert.ToHexString(await SHA256.HashDataAsync(content, token).ConfigureAwait(false)) != file.Sha256)
                throw new InvalidDataException("备份完整性验证失败。");
        }
        return manifest;
    }, token);
    private static void SnapshotSqlite(string source, string destination)
    {
        using var input = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = source, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        using var output = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        input.Open(); output.Open(); input.BackupDatabase(output);
    }
}
[JsonSerializable(typeof(BackupManifest))]
internal partial class BackupJsonContext : JsonSerializerContext;
