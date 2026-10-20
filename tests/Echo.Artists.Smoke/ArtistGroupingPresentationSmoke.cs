using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Application.Playback;
using Echo.Contracts.Library;
using Echo.Core;
using Echo.Core.Client;
using Echo.Core.Hosting;
using Echo.Core.Transport;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Library;

internal static partial class ArtistGroupingSmoke
{
    public static async Task CheckCoreAsync(string root)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var endpoint = new CoreEndpoint(root, Path.Combine(root, "grouping.sqlite"));
        await using var runtime = new CoreRuntime(new PlaybackRuntime(new ArtistDetailSmoke.AudioFixture()), endpoint.LibraryPath);
        var host = CoreServiceHost.RunAsync(runtime, endpoint, TimeSpan.FromMinutes(1), stop.Token);
        try
        {
            await using var first = await CoreConnection.ConnectAsync(endpoint, token: stop.Token);
            await using var second = await CoreConnection.ConnectAsync(endpoint, token: stop.Token);
            await CheckGroupsAsync(first.Library, stop.Token);
            await CheckGroupsAsync(second.Library, stop.Token);
        }
        finally { stop.Cancel(); await host; }
    }
    public static void Render(string root)
    {
        var database = LibraryDatabase.OpenAsync(Path.Combine(root, "grouping.sqlite")).GetAwaiter().GetResult();
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var shell = new ShellViewModel(); shell.AttachLibrary(database, artwork);
        var view = new ArtistsView(shell);
        var window = new Window { Content = view, Width = 1100, Height = 800 };
        try
        {
            window.Show(); Until(() => view.Model.Total == 4 && view.Viewport.RealizedCards > 0);
            Capture("song-artists.png");
            view.Model.GroupingIndex = 1;
            Until(() => !view.Model.IsBusy && view.Model.At(0)?.Grouping == ArtistGrouping.AlbumArtist);
            Check(Enumerable.Range(0, view.Model.Total).Any(index => view.Model.At(index)?.Name == "Alpha; Beta"),
                "The album-artist wall displays the complete joint credit.");
            Capture("album-artists.png");
            window.Width = 760; Capture("album-artists-narrow.png");
        }
        finally
        {
            window.Close(); view.Dispose(); artwork.DisposeAsync().AsTask().GetAwaiter().GetResult();
            database.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        void Capture(string name)
        {
            Settle(); using var bitmap = window.CaptureRenderedFrame() ?? throw new IOException("No artist wall frame.");
            bitmap.Save(Path.Combine(root, name), PngBitmapEncoderOptions.Default);
        }
        static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        static void Until(Func<bool> ready)
        {
            var timer = Stopwatch.StartNew();
            while (!ready()) { if (timer.Elapsed.TotalSeconds > 5) throw new TimeoutException("Artist grouping wall."); Settle(); Thread.Sleep(2); }
            Settle();
        }
    }
}
