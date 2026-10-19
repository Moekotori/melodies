using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Files;

namespace Echo.Core.Library;

public sealed partial class LibraryCore : IFolderOperations
{
    private readonly SemaphoreSlim _resourceCheck = new(1, 1);
    private FolderWatchCoordinator? _folderWatch;
    public event Action<string>? FolderChanged;

    public Task<ScanReport> ScanFolderAsync(FolderScanOptions options, IProgress<ScanProgress> progress, CancellationToken cancellation)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            if (_scan is { IsCompleted: false }) throw new InvalidOperationException("已有扫描正在运行，请先等待完成或取消。");
            return _scan = Task.Run(() => RunImportAsync(options.Path, progress, cancellation, options));
        }
    }

    public async Task<FolderIgnorePreview> PreviewFolderIgnoresAsync(string path, CancellationToken cancellation)
    {
        var rules = await GetFolderRulesAsync(cancellation).ConfigureAwait(false);
        var rule = rules.FirstOrDefault(item => LibraryPath.Key(item.Path) == LibraryPath.Key(Path.TrimEndingDirectorySeparator(LibraryPath.Canonical(path))));
        return await Task.Run(() =>
        {
            var ignore = new FolderIgnoreRules(path, rule?.Ignore ?? []); var samples = new List<string>(16); long examined = 0, excluded = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = rule?.Recursive ?? true,
                IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
            {
                cancellation.ThrowIfCancellationRequested(); if (++examined > 10000) return new FolderIgnorePreview(examined - 1, excluded, samples, true);
                if (!ignore.Matches(file)) continue; excluded++; if (samples.Count < 16) samples.Add(file);
            }
            return new FolderIgnorePreview(examined, excluded, samples, false);
        }, cancellation).ConfigureAwait(false);
    }

    public async Task CheckFolderResourcesAsync(FolderTrackQuery query, IProgress<FolderResourceProgress> progress, CancellationToken cancellation)
    {
        if (!await _resourceCheck.WaitAsync(0, cancellation).ConfigureAwait(false)) throw new InvalidOperationException("资源检查已在运行。");
        try
        {
            await Task.Run(async () =>
            {
            var database = await Database().WaitAsync(cancellation).ConfigureAwait(false); var artwork = new LocalAlbumArtwork();
            long checkedCount = 0, covers = 0, lyrics = 0;
            for (var offset = 0; ; offset += 200)
            {
                var page = await database.GetFolderTracksAsync(query with { Offset = offset, Revision = null, Search = null,
                    HideDuplicates = false, Insight = FolderInsight.All, LosslessOnly = false }, cancellation).ConfigureAwait(false);
                var facts = new List<FolderResourceFact>(page.Items.Count);
                foreach (var track in page.Items)
                {
                    cancellation.ThrowIfCancellationRequested(); var source = track.Source?.AudioPath ?? track.Path;
                    var hasCover = await artwork.ReadAsync(track.Source?.CoverPath ?? source, cancellation).ConfigureAwait(false) is { Length: > 0 };
                    var hasLyrics = HasLyrics(source);
                    if (hasCover) covers++; if (hasLyrics) lyrics++; checkedCount++; facts.Add(new(track.Id, hasCover, hasLyrics));
                }
                if (facts.Count > 0) await database.StoreFolderResourcesAsync(facts, cancellation).ConfigureAwait(false);
                progress.Report(new(checkedCount, page.Total, covers, lyrics)); if (page.Items.Count < 200) break;
            }
            }, cancellation).ConfigureAwait(false);
        }
        finally { _resourceCheck.Release(); }
        static bool HasLyrics(string path) => new[] { ".lrc", ".lrcx", ".ttml", ".srt", ".vtt", ".ass", ".krc", ".yrc", ".qrc", ".lys" }
            .Any(extension => File.Exists(Path.ChangeExtension(path, extension)));
    }

    public async Task SetFolderWatchAsync(string path, bool enabled, CancellationToken cancellation)
    {
        var rules = await GetFolderRulesAsync(cancellation).ConfigureAwait(false); var rule = rules.FirstOrDefault(item => LibraryPath.Key(item.Path) == LibraryPath.Key(path)) ?? new(path);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _shutdown.Token);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _folderWatch ??= new(ScanFolderAsync, changed => FolderChanged?.Invoke(changed));
        }
        await _folderWatch.SetAsync(path, enabled, lifetime.Token).ConfigureAwait(false);
        await SaveFolderRuleAsync(rule with { Watch = enabled }, lifetime.Token).ConfigureAwait(false);
    }
}
