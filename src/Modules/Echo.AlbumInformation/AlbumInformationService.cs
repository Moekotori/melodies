using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Echo.Core.Contracts;

namespace Echo.AlbumInformation;

// Core owns provider throttling, bounded cache and shared in-flight work.
public sealed class AlbumInformationService : IAlbumInformationClient, IDisposable
{
    private readonly MetadataHttp _http;
    private readonly MusicBrainzProvider _musicBrainz;
    private readonly WikipediaProvider _wikipedia;
    private readonly SemaphoreSlim _budget = new(2, 2);
    private readonly object _gate = new();
    private readonly Dictionary<string, Task<AlbumInformationResult>> _pending = [];
    private readonly string _cacheDirectory;
    public AlbumInformationService(string cacheDirectory, HttpMessageHandler? handler = null)
    { _cacheDirectory = cacheDirectory; _http = new(handler); _musicBrainz = new(_http); _wikipedia = new(_http); }
    public Task<AlbumInformationResult> ReadAsync(AlbumInformationRequest request, CancellationToken cancellation = default)
    {
        var album = request.Album;
        if (string.IsNullOrWhiteSpace(album.Title) || string.IsNullOrWhiteSpace(album.Artist) || album.Title.Length > 512 || album.Artist.Length > 512 || album.Language.Length > 16)
            throw new ArgumentException("Invalid album identity.");
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v3|{album.Title}|{album.Artist}|{album.Year}|{album.TrackCount}|{album.Language}")));
        Task<AlbumInformationResult> task;
        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out task!))
            {
                if (_pending.Count >= 16) throw new IOException("资料服务繁忙，请稍后重试。");
                task = FetchAsync(key, request); _pending[key] = task;
                _ = RemovePendingAsync(key, task);
            }
        }
        // A disconnected Control stops waiting; the accepted Core operation still populates the cache.
        return task.WaitAsync(cancellation);
    }
    private async Task RemovePendingAsync(string key, Task<AlbumInformationResult> task)
    {
        try { await task.ConfigureAwait(false); } catch (Exception) { }
        finally { lock (_gate) if (_pending.TryGetValue(key, out var pending) && ReferenceEquals(pending, task)) _pending.Remove(key); }
    }
    private async Task<AlbumInformationResult> FetchAsync(string key, AlbumInformationRequest request)
    {
        await _budget.WaitAsync().ConfigureAwait(false);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(65)); var token = timeout.Token;
            var cached = await ReadCacheAsync(key, token).ConfigureAwait(false);
            var ttl = cached?.Status == "ready" ? TimeSpan.FromDays(7) : TimeSpan.FromMinutes(15);
            if (!request.Refresh && cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < ttl) return cached;
            var errors = new List<string>();
            var musicTask = ProviderAsync(token => _musicBrainz.ReadAsync(request.Album, token), "MusicBrainz 暂时无法连接，请稍后刷新。", token);
            var albumTask = ProviderAsync(token => _wikipedia.ReadAsync(request.Album, false, token), "专辑百科暂时无法连接。", token);
            var artistTask = ProviderAsync(token => _wikipedia.ReadAsync(request.Album, true, token), "艺术家百科暂时无法连接。", token);
            await Task.WhenAll(musicTask, albumTask, artistTask).ConfigureAwait(false);
            var music = (await musicTask.ConfigureAwait(false)).Value;
            var albumArticle = (await albumTask.ConfigureAwait(false)).Value;
            var artistArticle = (await artistTask.ConfigureAwait(false)).Value;
            foreach (var message in new[] { musicTask.Result.Error, albumTask.Result.Error, artistTask.Result.Error }) if (message is not null) errors.Add(message);
            var hasContent = music is not null || albumArticle is not null || artistArticle is not null;
            if (!hasContent && cached is not null && cached.Status is "ready" or "partial")
                return cached with { Status = "stale", Errors = errors.Append("当前显示已缓存资料，联网恢复后可刷新。").ToArray() };
            var sources = new List<string>(); if (music is not null) sources.Add("MusicBrainz"); if (albumArticle is not null || artistArticle is not null) sources.Add("Wikipedia");
            var links = music?.Links.ToList() ?? [];
            foreach (var article in new[] { albumArticle, artistArticle }.OfType<InformationArticle>()) links.Add(new("Wikipedia", article.Title, article.Url, "百科"));
            var result = new AlbumInformationResult(hasContent ? errors.Count > 0 ? "partial" : "ready" : errors.Count > 0 ? "error" : "empty", DateTimeOffset.UtcNow,
                sources.ToArray(), music?.Release?.Title, music?.Release?.Artist, music?.Confidence, music?.Ambiguous ?? false,
                music?.Release, music?.Versions ?? [], music?.Credits ?? [], links.Take(20).ToArray(), albumArticle, artistArticle, errors.ToArray());
            await WriteCacheAsync(key, result, CancellationToken.None).ConfigureAwait(false); return result;
        }
        finally { _budget.Release(); }
    }
    private static async Task<(T? Value, string? Error)> ProviderAsync<T>(Func<CancellationToken, Task<T?>> fetch, string message, CancellationToken lifetime) where T : class
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        deadline.CancelAfter(TimeSpan.FromSeconds(24));
        try { return (await fetch(deadline.Token).ConfigureAwait(false), null); }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException) { return (null, message); }
    }
    private async Task<AlbumInformationResult?> ReadCacheAsync(string key, CancellationToken token)
    {
        try
        {
            var path = Path.Combine(_cacheDirectory, key + ".json");
            if (!File.Exists(path) || new FileInfo(path).Length > CoreMetadataProtocol.MaxBytes) return null;
            var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(bytes, CoreMetadataJson.Default.AlbumInformationResult);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    private async Task WriteCacheAsync(string key, AlbumInformationResult result, CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, CoreMetadataJson.Default.AlbumInformationResult);
            if (bytes.Length > CoreMetadataProtocol.MaxBytes) return;
            var target = Path.Combine(_cacheDirectory, key + ".json");
            await File.WriteAllBytesAsync(target + ".tmp", bytes, token).ConfigureAwait(false); File.Move(target + ".tmp", target, true);
            var files = new DirectoryInfo(_cacheDirectory).EnumerateFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).ToArray();
            long size = 0;
            for (var index = 0; index < files.Length; index++) { size += files[index].Length; if (index >= 128 || size > 16 * 1024 * 1024) files[index].Delete(); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public void Dispose() { _http.Dispose(); _budget.Dispose(); }
}
