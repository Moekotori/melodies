using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Library;

namespace Echo.ArtistMetadata;

public sealed class ArtistInformationService(string cacheDirectory, HttpMessageHandler? handler = null) : IArtistInformationClient, IAsyncDisposable
{
    private readonly ArtistHttp http = new(handler);
    private readonly SemaphoreSlim budget = new(2, 2);
    private readonly CancellationTokenSource shutdown = new();
    private readonly object gate = new();
    private readonly Dictionary<string, Work> pending = [];
    private readonly HashSet<Task> running = [];
    private sealed class Work(CancellationTokenSource cancellation)
    {
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public Task<ArtistInformation> Task { get; set; } = null!;
        public int Waiters;
    }
    public async Task<ArtistInformation> GetArtistInformationAsync(ArtistInformationRequest request, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 512 || request.Name.Contains('|') || request.Language is not ("zh" or "en"))
            throw new ArgumentException("Invalid artist identity.");
        cancellation.ThrowIfCancellationRequested();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v5|" + request.Name.Trim() + "|" + request.Language + "|" + request.IncludeFacts)));
        Work work;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(shutdown.IsCancellationRequested, this);
            if (!pending.TryGetValue(key, out work!))
            {
                if (pending.Count >= 8 || running.Count >= 8) throw new IOException("艺人资料请求较多，请稍后刷新。");
                work = new(CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token));
                work.Cancellation.CancelAfter(TimeSpan.FromSeconds(32));
                work.Task = Task.Run(() => FetchAsync(key, request, work.Cancellation.Token));
                pending.Add(key, work); running.Add(work.Task);
                _ = RetireAsync(work);
            }
            work.Waiters++;
        }
        try { return await work.Task.WaitAsync(cancellation).ConfigureAwait(false); }
        finally
        {
            lock (gate)
            {
                if (--work.Waiters == 0)
                {
                    if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, work)) pending.Remove(key);
                    if (!work.Task.IsCompleted) work.Cancellation.Cancel();
                    else work.Cancellation.Dispose();
                }
            }
        }
    }
    private async Task RetireAsync(Work work)
    {
        try { await work.Task.ConfigureAwait(false); } catch (Exception) { }
        finally { lock (gate) { running.Remove(work.Task); if (work.Waiters == 0) work.Cancellation.Dispose(); } }
    }
    private async Task<ArtistInformation> FetchAsync(string key, ArtistInformationRequest request, CancellationToken token)
    {
        await budget.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var cached = await ReadCacheAsync(key, token).ConfigureAwait(false);
            if (!request.Refresh && cached is not null && DateTimeOffset.UtcNow - cached.FetchedAt < (cached.EventStatus == "ready" && cached.Status != "empty" ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1))) return cached;
            var providers = new ArtistProviders(http);
            var bioTask = ProviderAsync(t => providers.BiographyAsync(request, t), "Wikipedia 暂时无法连接。", token);
            var musicTask = ProviderAsync(t => providers.MusicAsync(request, t), "MusicBrainz 暂时无法连接。", token);
            var concertTask = ProviderAsync(t => new ArtistConcertProviders(http).ReadAsync(request.Name, t), "演唱会来源暂时无法连接。", token, 12);
            await Task.WhenAll(bioTask, musicTask, concertTask).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var bio = bioTask.Result.Value; var music = musicTask.Result.Value;
            var errors = new[] { bioTask.Result.Error, musicTask.Result.Error }.OfType<string>().Concat(music?.Errors ?? []).ToList();
            if (bio is null && music is null && cached is not null && bioTask.Result.Error is not null && musicTask.Result.Error is not null && concertTask.Result.Value?.Checked != true)
                return cached with { Status = "stale", EventStatus = "stale", EventFailures = ["MusicBrainz", "Eventernote", "eplus"], Errors = errors.Append("当前显示离线缓存资料。").ToArray() };
            var concerts = ArtistConcertProviders.Merge(music, concertTask.Result.Value, cached, musicTask.Result.Error is not null);
            var cachedBiography = bio is null && bioTask.Result.Error is not null ? cached?.Biography : null;
            var cachedMusic = music is null && musicTask.Result.Error is not null ? cached : null;
            bio ??= cachedBiography;
            music ??= cachedMusic;
            if (cachedBiography is not null || cachedMusic is not null || concerts.Status == "stale") errors.Add("部分资料来自缓存，请联网后刷新。");
            var links = (music?.Links ?? []).ToList();
            if (bio is not null) links.Insert(0, new("Wikipedia", bio.Url));
            var entityId = bio?.WikidataId;
            if (!WikidataArtistProvider.ValidId(entityId))
                entityId = links.Select(link => Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Host is "www.wikidata.org" or "wikidata.org" ? uri.AbsolutePath.Split('/').LastOrDefault() : null).FirstOrDefault(WikidataArtistProvider.ValidId);
            var facts = (music?.Facts ?? []).ToList();
            ArtistPublicFacts? publicFacts = null;
            if (WikidataArtistProvider.ValidId(entityId))
            {
                var enrichment = await ProviderAsync(t => new WikidataArtistProvider(http).ReadAsync(entityId!, request.Language, t, request.IncludeFacts), "Wikidata 暂时无法连接。", token, 6).ConfigureAwait(false);
                facts.AddRange(enrichment.Value?.Facts ?? []);
                if (bio is not null && bio.WikidataId == entityId && enrichment.Value?.Description is { Length: > 0 } description)
                    bio = bio with { Description = description };
                if (request.IncludeFacts && enrichment.Value is { } data)
                    publicFacts = data with { Name = data.Name == entityId ? request.Name : data.Name, Match = bio?.WikidataId == entityId ? "wikipedia" : "musicbrainz-id" };
                if (enrichment.Error is { } message) errors.Add(message);
                if (enrichment.Value is not null) links.Add(new("Wikidata", "https://www.wikidata.org/wiki/" + entityId));
            }
            if (bio?.ThumbnailUrl is { } thumbnail)
            {
                var reference = await new ArtistImageStore(Path.Combine(cacheDirectory, "images"), http).StoreAsync(thumbnail, token).ConfigureAwait(false);
                bio = bio with { ArtworkReference = reference };
            }
            var hasContent = bio is not null || music is not null || concerts.Events.Count > 0;
            var result = new ArtistInformation(hasContent ? errors.Count > 0 ? "partial" : "ready" : errors.Count > 0 ? "error" : "empty", DateTimeOffset.UtcNow,
                bio, facts.DistinctBy(fact => fact.Label).Take(16).ToArray(), music?.Relations ?? [], concerts.Events, links, errors,
                concerts.Status, publicFacts ?? cachedMusic?.PublicFacts, concerts.Failures);
            // Failed and incomplete work never replaces a healthy disk entry.
            if (result.Status == "ready" || result.Status == "empty" && concerts.Status == "ready") await WriteCacheAsync(key, result, token).ConfigureAwait(false);
            return result;
        }
        finally { budget.Release(); }
    }
    private static async Task<(T? Value, string? Error)> ProviderAsync<T>(Func<CancellationToken, Task<T?>> fetch, string message, CancellationToken token, int seconds = 20) where T : class
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { return (await fetch(deadline.Token).ConfigureAwait(false), null); }
        catch (Exception error) when (error is HttpRequestException or IOException or System.Text.Json.JsonException or OperationCanceledException)
        { token.ThrowIfCancellationRequested(); return (null, message); }
    }
    public Task<byte[]?> ReadImageAsync(string reference, CancellationToken token)
        => new ArtistImageStore(Path.Combine(cacheDirectory, "images"), http).ReadAsync(reference, token);
    private async Task<ArtistInformation?> ReadCacheAsync(string key, CancellationToken token)
    {
        try
        {
            var file = new FileInfo(Path.Combine(cacheDirectory, key + ".json"));
            if (!file.Exists || file.Length > 128 * 1024) return null;
            var bytes = await File.ReadAllBytesAsync(file.FullName, token).ConfigureAwait(false);
            return JsonSerializer.Deserialize(bytes, ArtistInformationJson.Default.ArtistInformation);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    private async Task WriteCacheAsync(string key, ArtistInformation result, CancellationToken token)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(result, ArtistInformationJson.Default.ArtistInformation);
            if (bytes.Length > 128 * 1024) return;
            Directory.CreateDirectory(cacheDirectory);
            var target = Path.Combine(cacheDirectory, key + ".json");
            await File.WriteAllBytesAsync(target + ".tmp", bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested(); File.Move(target + ".tmp", target, true);
            // At most 64 entries / 8 MiB of serialized payloads, with no resident article cache.
            foreach (var file in new DirectoryInfo(cacheDirectory).EnumerateFiles("*.json").OrderByDescending(f => f.LastWriteTimeUtc).Skip(64)) file.Delete();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (gate) { shutdown.Cancel(); tasks = running.ToArray(); }
        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch (Exception) { }
        http.Dispose(); budget.Dispose(); shutdown.Dispose();
    }
}
[JsonSerializable(typeof(ArtistInformation))]
internal partial class ArtistInformationJson : JsonSerializerContext;
