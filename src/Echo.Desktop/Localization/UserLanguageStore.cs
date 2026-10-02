using System.Text.Json;

namespace Echo.Desktop.Localization;

public sealed record UserLanguageIssue(string FileName, string Detail);
public sealed record UserLanguageFile(string Path, LanguagePack Pack);
public sealed record UserLanguageSnapshot(LanguageCatalog Catalog, IReadOnlyList<UserLanguageFile> Files, IReadOnlyList<UserLanguageIssue> Issues);
public sealed record UserLanguageImport(string Code, UserLanguageSnapshot Snapshot);

// Only explicit/startup I/O. One active operation, no queued jobs or watcher; parsing never runs on the UI thread.
public sealed class UserLanguageStore(string directory, LanguageCatalog? builtIn = null)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    private readonly LanguageCatalog _builtIn = builtIn ?? LanguageCatalog.Default;
    private int _busy;
    private const int MaximumFiles = 16;

    public Task<UserLanguageSnapshot> ReadAsync(CancellationToken cancellation) => RunAsync(() => Read(cancellation), cancellation);

    public Task<UserLanguageImport> ImportAsync(Stream source, CancellationToken cancellation) => RunAsync(() =>
    {
        var pack = LanguageCatalog.ReadPack(source, cancellation); _builtIn.ValidatePack(pack);
        var snapshot = Read(cancellation);
        var target = snapshot.Files.FirstOrDefault(file => file.Pack.Code.Equals(pack.Code, StringComparison.OrdinalIgnoreCase))?.Path
            ?? Path.Combine(DirectoryPath, "language-" + pack.Code + ".json");
        var count = Directory.Exists(DirectoryPath) ? Directory.EnumerateFiles(DirectoryPath, "*.json").Take(MaximumFiles + 1).Count() : 0;
        if (count > MaximumFiles || count == MaximumFiles && !File.Exists(target)) throw new InvalidDataException("At most 16 user language files are supported.");
        var bytes = LanguagePackJson.Serialize(pack);
        cancellation.ThrowIfCancellationRequested(); Directory.CreateDirectory(DirectoryPath);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes); cancellation.ThrowIfCancellationRequested();
            if (File.Exists(target))
            {
                byte[] original;
                using (var previous = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (previous.Length > 256 * 1024) throw new InvalidDataException("Existing language file exceeds 256 KiB; move it before importing.");
                    original = new byte[(int)previous.Length]; previous.ReadExactly(original);
                }
                File.WriteAllBytes(target + ".bak", original);
            }
            File.Move(temporary, target, overwrite: true);
            // Commit is confirmed. Return the resulting snapshot even if cancellation races after the rename.
            return new UserLanguageImport(pack.Code, Read(CancellationToken.None));
        }
        finally { try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }, cancellation);

    public Task ExportAsync(Stream destination, LanguageCatalog catalog, string code, CancellationToken cancellation) => RunAsync(() =>
    {
        var selected = catalog.Resolve(code);
        var strings = catalog.Fallback.Strings.ToDictionary(pair => pair.Key,
            pair => selected.Strings.GetValueOrDefault(pair.Key) ?? pair.Value, StringComparer.Ordinal);
        var bytes = LanguagePackJson.Serialize(new LanguagePack(selected.Code, selected.NativeName, strings));
        cancellation.ThrowIfCancellationRequested();
        if (destination.CanSeek) { destination.SetLength(0); destination.Position = 0; }
        destination.Write(bytes); destination.Flush(); return true;
    }, cancellation);

    public Task EnsureDirectoryAsync(CancellationToken cancellation) => RunAsync(() =>
    { cancellation.ThrowIfCancellationRequested(); Directory.CreateDirectory(DirectoryPath); return true; }, cancellation);

    private UserLanguageSnapshot Read(CancellationToken cancellation)
    {
        var files = new List<UserLanguageFile>(); var issues = new List<UserLanguageIssue>();
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (!Directory.Exists(DirectoryPath)) return new(_builtIn, files, issues);
            var paths = Directory.EnumerateFiles(DirectoryPath, "*.json", SearchOption.TopDirectoryOnly).Take(MaximumFiles + 1).ToArray();
            if (paths.Length > MaximumFiles) return new(_builtIn, files, [new("languages", "At most 16 user language files are supported.")]);
            var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths.Order(StringComparer.OrdinalIgnoreCase))
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var pack = LanguageCatalog.ReadPack(stream, cancellation); _builtIn.ValidatePack(pack);
                    if (files.Any(file => file.Pack.Code.Equals(pack.Code, StringComparison.OrdinalIgnoreCase)) || duplicates.Contains(pack.Code))
                    { duplicates.Add(pack.Code); issues.Add(new(Path.GetFileName(path), "Duplicate culture code: " + pack.Code)); }
                    else files.Add(new(path, pack));
                }
                catch (Exception error) when (IsPackError(error)) { issues.Add(new(Path.GetFileName(path), Detail(error))); }
            }
            foreach (var duplicate in files.Where(file => duplicates.Contains(file.Pack.Code)).ToArray())
            { files.Remove(duplicate); issues.Add(new(Path.GetFileName(duplicate.Path), "Duplicate culture code: " + duplicate.Pack.Code)); }
            return new(_builtIn.WithUserPacks(files.Select(file => file.Pack)), files, issues);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return new(_builtIn, [], [new("languages", Detail(error))]); }
    }

    private async Task<T> RunAsync<T>(Func<T> action, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) throw new InvalidOperationException("A language file operation is already running.");
        try { return await Task.Run(action, cancellation).ConfigureAwait(false); }
        finally { Volatile.Write(ref _busy, 0); }
    }
    internal static bool IsPackError(Exception error) => error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
        or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException;
    private static string Detail(Exception error) => error.Message.Length > 240 ? error.Message[..240] : error.Message;
}
