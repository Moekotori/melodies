using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Search;
using Echo.Desktop.Shell;
using static QueueProjectionSmoke;

internal static class GlobalSearchConcurrencySmoke
{
    public static void Run()
    {
        var library = new ControlledSearchLibrary();
        var shell = new ShellViewModel();
        var artwork = new AlbumArtworkCache(library); shell.AttachLibrary(library, artwork);
        using var model = new GlobalSearchViewModel(shell);
        try
        {
            model.Search = "__match__"; Wait(() => library.Calls.Count == 2);
            library.Calls[1].Complete(); // Slow tracks cannot hold up albums/artists/genres.
            Wait(() => model.Hits.Any(hit => hit.Target is AlbumRow) && library.Calls.Count == 3);
            library.Calls[2].Complete(); Wait(() => library.Calls.Count == 4);
            library.Calls[3].Complete(); library.Calls[0].Complete(); Wait(() => !model.IsLoading);
            Check(Content().Select(hit => hit.Target.GetType()).SequenceEqual(new[] { typeof(TrackRow), typeof(AlbumRow), typeof(ArtistRow), typeof(GenreRow) }), "completion order does not change equal-score source order");
            library.Count = 16;
            model.Search = "__cap__"; Finish("__cap__");
            Check(Content().Length == 24 && Content().Take(16).All(hit => hit.Target is TrackRow), "24-result cap preserves existing ranking");
            model.Search = "__old__"; Wait(() => library.Calls.Count(call => call.Query == "__old__") == 2);
            model.Search = "__new__";
            foreach (var call in library.Calls.Where(call => call.Query == "__old__"))
            { Check(call.Token.IsCancellationRequested, "old query token cancelled"); call.Complete(); }
            Finish("__new__");
            Check(Content().All(hit => hit.Title == "__new__") && library.MaximumActive == 2, "stale results ignored and cross-generation concurrency bounded");
            model.Search = "/direct"; Finish("/direct");
            Check(library.Calls.Count(call => call.Query == "/direct") == 1 && Content().All(hit => hit.Target is TrackRow), "path search remains tracks-only");
            model.Search = "__closed__"; Wait(() => library.Calls.Count(call => call.Query == "__closed__") == 2);
            model.Dispose();
            foreach (var call in library.Calls.Where(call => call.Query == "__closed__")) call.Complete();
            Wait(() => model.Hits.Count == 0 && !model.IsLoading);
            Console.WriteLine("PASS global search: early partial results, stable ties/cap, two-call limit, stale generations, path mode and close cancellation");
        }
        finally { model.Dispose(); shell.Settings.Dispose(); shell.ReleaseLocalization(); Pump(artwork.DisposeAsync().AsTask()); }
        GlobalSearchHit[] Content() => model.Hits.Where(hit => hit.Target is not SearchQueryTarget).ToArray();
        void Finish(string query)
        {
            Wait(() =>
            {
                foreach (var call in library.Calls.Where(call => call.Query == query)) call.Complete();
                return !model.IsLoading;
            });
        }
    }
}
