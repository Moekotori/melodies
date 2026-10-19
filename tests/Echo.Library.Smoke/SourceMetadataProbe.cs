using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Metadata;

internal static class SourceMetadataProbe
{
    public static async Task RunAsync(CancellationToken token)
    {
        var reader = new SourceMetadataReader();
        var source = new TrackSource("cue", Path.GetFullPath("source.flac"), Path.GetFullPath("source.cue"), "stamp", 10, 20, 10, 20,
            AlbumArtist: "Sheet artist", StartSeconds: 1, EndSeconds: 2);
        var file = new FileSnapshot(source.DefinitionPath + "#cueTrack=1", "virtual", 10, 20, Source: source);
        var reads = 0;
        ValueTask<TrackMetadata> Read(FileSnapshot _, bool allow, CancellationToken __)
        {
            reads++;
            return ValueTask.FromResult(new TrackMetadata("Physical", allow ? "Permissive" : "Strict", "Album", "Physical credit", 4, "FLAC", 48000, 16));
        }
        var first = await reader.ReadAsync(file, source, Read, token);
        Check(first.Artist == "Sheet artist" && first.AlbumArtist == "Sheet artist" && first.DurationSeconds == 1, "CUE sheet performer inheritance");
        var performer = await reader.ReadAsync(file, source with { Artist = "Track artist", AlbumArtist = null }, Read, token);
        Check(performer.Artist == "Track artist" && performer.AlbumArtist == "Physical credit" && reads == 1,
            "track performer must not replace album identity credit or trigger another read");
        var osu = await reader.ReadAsync(file, source with { Kind = "osu", AlbumArtist = null }, Read, token);
        var cue = await reader.ReadAsync(file, source with { AlbumArtist = null }, Read, token);
        Check(osu.Artist == "Permissive" && cue.Artist == "Strict" && reads == 2, "permissive osu metadata must not bypass strict CUE policy");

        reader.Clear(); reads = 0;
        ValueTask<TrackMetadata> Broken(FileSnapshot _, bool __, CancellationToken ___)
        { reads++; throw new MetadataReadException("invalid_flac_metadata"); }
        for (var i = 0; i < 40; i++)
        {
            try { await reader.ReadAsync(file, source, Broken, token); throw new Exception("Bad source accepted"); }
            catch (MetadataReadException error) when (error.Code == "invalid_flac_metadata") { }
        }
        Check(reads == 1, "one broken CUE source was parsed for every virtual track");
        await reader.ReadAsync(file, source with { AudioTicks = 21 }, Read, token);
        Check(reads == 2, "changed source revision must invalidate cached failure");
        reader.Clear(); reads = 0;
        ValueTask<TrackMetadata> Truncated(FileSnapshot _, bool __, CancellationToken ___)
        { reads++; throw new EndOfStreamException(); }
        for (var i = 0; i < 2; i++)
        {
            try { await reader.ReadAsync(file, source, Truncated, token); throw new Exception("Truncated source accepted"); }
            catch (MetadataReadException error) when (error.Code == "metadata_io_failed") { }
        }
        Check(reads == 1, "truncated managed headers were reopened for each CUE track");
        reader.Clear(); reads = 0;
        ValueTask<TrackMetadata> Unconfirmed(FileSnapshot _, bool __, CancellationToken ___)
        { reads++; throw new MetadataReadException("native_cleanup_unconfirmed"); }
        for (var i = 0; i < 2; i++)
        {
            try { await reader.ReadAsync(file, source, Unconfirmed, token); throw new Exception("Unconfirmed cleanup accepted"); }
            catch (MetadataReadException error) when (error.IsInfrastructureFailure) { }
        }
        Check(reads == 2, "infrastructure faults were cached as file failures");
        reader.Clear(); reads = 0;
        ValueTask<TrackMetadata> Unknown(FileSnapshot _, bool __, CancellationToken ___)
        { reads++; return ValueTask.FromResult(new TrackMetadata("Raw", "Artist", "Album", "", 0, "TRUEHD", 48000, 24)); }
        Check((await reader.ReadAsync(file, source, Unknown, token)).DurationSeconds == 1, "known CUE boundary over unknown physical duration");
        Check((await reader.ReadAsync(file, source with { EndSeconds = null }, Unknown, token)).DurationSeconds == 0, "unknown final duration must remain unknown");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await reader.ReadAsync(file, source, Unknown, cancelled.Token); throw new Exception("Cached read ignored cancellation"); }
        catch (OperationCanceledException) { }
        Check(reads == 1, "cancellation must not start another physical read");
        Console.WriteLine("PASS source metadata: credits, policy isolation, failure sharing/revision, infrastructure faults, unknown durations and cached cancellation");
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
