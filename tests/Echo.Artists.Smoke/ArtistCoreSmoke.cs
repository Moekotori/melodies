using Echo.Application.Playback;
using Echo.Core;
using Echo.Core.Client;
using Echo.Core.Hosting;
using Echo.Core.Transport;

internal static class ArtistCoreSmoke
{
    public static async Task RunAsync(string root)
    {
        var cache = Path.Combine(root, "artist-information"); Directory.CreateDirectory(cache);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "providers"), "*.json"))
            File.Copy(file, Path.Combine(cache, Path.GetFileName(file)), true);
        Directory.CreateDirectory(Path.Combine(cache, "images"));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "providers", "images"), "*.img"))
            File.Copy(file, Path.Combine(cache, "images", Path.GetFileName(file)), true);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var endpoint = new CoreEndpoint(root, Path.Combine(root, "artists.sqlite"));
        await using var runtime = new CoreRuntime(new PlaybackRuntime(new ArtistDetailSmoke.AudioFixture()), endpoint.LibraryPath);
        var host = CoreServiceHost.RunAsync(runtime, endpoint, TimeSpan.FromMinutes(1), stop.Token);
        try
        {
            await using var first = await CoreConnection.ConnectAsync(endpoint, token: stop.Token);
            var second = await CoreConnection.ConnectAsync(endpoint, token: stop.Token);
            var artist = (await first.Library.GetArtistsAsync(new(Search: "Aurora"), stop.Token)).Items.Single();
            var insights = await first.Library.GetArtistInsightsAsync(artist.Id, artist.Grouping, stop.Token);
            var other = await second.Library.GetArtistInsightsAsync(artist.Id, artist.Grouping, stop.Token);
            Check(insights.Plays == 2 && other.Plays == insights.Plays && other.Revision == insights.Revision, "Independent Controls must observe one Core's artist state.");
            var information = await first.Library.GetArtistInformationAsync(new("Aurora"), stop.Token);
            Check(information.Biography is not null && information.Facts.Count > 0, "Online artist information must travel through the typed Core RPC.");
            Check(first.Artwork is Echo.Contracts.Library.IArtistArtwork portraits && information.Biography?.ArtworkReference is { } reference
                && await portraits.ReadArtistAsync("online:" + reference, stop.Token) is { Length: > 0 }, "Core image references travel through the resource IPC without Control-side downloads.");
            Check(information.Relations.Single().LocalArtist?.Name == "Björk", "Core resolves online relationships against its library and returns typed navigation targets.");
            await second.DisposeAsync();
            await using var reconnected = await CoreConnection.ConnectAsync(endpoint, token: stop.Token);
            Check((await reconnected.Library.GetArtistInsightsAsync(artist.Id, artist.Grouping, stop.Token)).Plays == 2,
                "Disconnecting a Control must leave the Core owner and artist data available.");
            Console.WriteLine("PASS artist Core IPC: production dispatch, AOT DTOs, two Controls sharing one owner, metadata cache, detach and reconnect. Server runs in an isolated test process; no real audio device.");
        }
        finally { stop.Cancel(); await host; }
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
