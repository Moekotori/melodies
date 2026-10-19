using Echo.Contracts.Library;

internal sealed class ControlledSearchLibrary : IAlbumLibrary, ITrackLibrary, IArtistLibrary, IGenreLibrary, IAlbumArtwork
{
    public sealed record Call(string Kind, string Query, TaskCompletionSource Completion, CancellationToken Token)
    { public void Complete() => Completion.TrySetResult(); }
    public List<Call> Calls { get; } = [];
    private int active;
    public int MaximumActive { get; private set; }
    public int Count { get; set; } = 1;
    private async Task<T> Read<T>(string kind, string? query, CancellationToken token, Func<T> result)
    {
        active++; MaximumActive = Math.Max(MaximumActive, active);
        var call = new Call(kind, query ?? "", new(TaskCreationOptions.RunContinuationsAsynchronously), token);
        Calls.Add(call);
        try { await call.Completion.Task; return result(); } // Deliberately ignores cancellation to exercise late results.
        finally { active--; }
    }
    public Task<TrackPage> GetTracksAsync(TrackQuery query, CancellationToken token = default)
        => query.Filter?.FavoriteOnly == true ? Task.FromResult(new TrackPage([], null, 1))
        : Read("tracks", query.Search, token, () => new TrackPage(Enumerable.Range(0, Count).Select(i => new TrackRow(
            "track" + i, "fixture.flac", query.Search!, "Artist", "Album", 100, "FLAC", 48000, 24, "sort")).ToArray(), null, 1));
    public Task<AlbumPage> GetAlbumsAsync(AlbumQuery query, CancellationToken token = default)
        => Read("albums", query.Search, token, () => new AlbumPage(Enumerable.Range(0, Count).Select(i => new AlbumRow(
            "album" + i, query.Search!, "Artist", 1, 100, null, "fixture.flac", 1)).ToArray(), Count, 1));
    public Task<ArtistPage> GetArtistsAsync(ArtistQuery query, CancellationToken token = default)
        => Read("artists", query.Search, token, () => new ArtistPage([new("artist", query.Search!, 1, 1, 100, "fixture", ArtistGrouping.Credits)], 1, 1));
    public Task<GenrePage> GetGenresAsync(GenreQuery query, CancellationToken token = default)
        => Read("genres", query.Search, token, () => new GenrePage([new("genre", query.Search!, 1, 1, 100, "fixture")], 1, 1));
    public Task<byte[]?> ReadAsync(string samplePath, CancellationToken cancellation) => Task.FromResult<byte[]?>(null);
    public Task<TrackPage> GetAlbumTracksAsync(AlbumTrackQuery query, CancellationToken token = default) => throw new NotSupportedException();
    public Task<TrackPage> GetArtistTracksAsync(ArtistTrackQuery query, CancellationToken token = default) => throw new NotSupportedException();
    public Task<AlbumPage> GetArtistAlbumsAsync(string id, ArtistGrouping grouping, AlbumQuery query, CancellationToken token = default) => throw new NotSupportedException();
    public Task<TrackPage> GetGenreTracksAsync(GenreTrackQuery query, CancellationToken token = default) => throw new NotSupportedException();
    public Task<AlbumPage> GetGenreAlbumsAsync(string id, AlbumQuery query, CancellationToken token = default) => throw new NotSupportedException();
}
