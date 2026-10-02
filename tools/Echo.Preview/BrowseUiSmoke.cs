using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Features.Songs;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Library;

internal static class BrowseUiSmoke
{
    public static void Run(string path, string output)
    {
        Directory.CreateDirectory(output);
        var opening = LibraryDatabase.OpenAsync(path); Wait(() => opening.IsCompleted);
        var db = opening.GetAwaiter().GetResult();
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var shell = new ShellViewModel(); shell.AttachLibrary(db, artwork); shell.Text.SetLanguage("zh-CN");
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var window = new MainWindow { DataContext = shell, Width = 1440, Height = 900 };
        try
        {
            window.Show(); shell.NavigateCommand.Execute("albums");
            var view = window.GetVisualDescendants().OfType<AlbumsView>().Single(); var model = view.Model;
            Wait(() => !model.IsLoading && model.Total > 0);
            var control = view.GetVisualDescendants().OfType<AlbumSortControl>().Single();
            var sort = control.FindControl<Button>("SortButton")!; Click(sort);
            Check(sort.Flyout!.IsOpen, "sort menu did not open");
            var content = (Control)((Flyout)sort.Flyout).Content!;
            var playCount = content.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("libraryOption") && button.DataContext is AlbumSortOption { Field: AlbumSort.PlayCount });
            Click(playCount); Wait(() => !model.IsLoading);
            Check(model.SortIndex == (int)AlbumSort.PlayCount && model.Descending && !sort.Flyout.IsOpen, "native sort selection");
            Click(sort); Save("albums-sort-light.png"); SaveMenu((Flyout)sort.Flyout!, "albums-sort-detail.png");
            var direction = content.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("libraryDirection") && button.DataContext is AlbumSortOption { Field: AlbumSort.PlayCount });
            Click(direction); Wait(() => !model.IsLoading);
            Check(!model.Descending && sort.Flyout.IsOpen, "inline direction input and menu lifetime");
            Click(direction); Wait(() => !model.IsLoading); shell.IsDark = true; Save("albums-sort-dark.png"); shell.IsDark = false; sort.Flyout.Hide();
            model.SelectSort(AlbumSort.Random); Wait(() => !model.IsLoading);
            var first = Enumerable.Range(0, 200).Select(index => model.At(index)?.Id).ToArray();
            var load = model.LoadRangeAsync(9600, 200); Wait(() => load.IsCompleted); load.GetAwaiter().GetResult();
            Check(model.ResidentAlbums <= 600 && Enumerable.Range(0, 200).Select(index => model.At(index)?.Id).SequenceEqual(first), "random pages changed or were unbounded");
            var filter = view.FindControl<Button>("FilterButton")!; Click(filter);
            var filterContent = (Control)((Flyout)filter.Flyout!).Content!;
            var panel = filterContent.GetVisualDescendants().OfType<LibraryFilterView>().Single();
            var formats = panel.GetVisualDescendants().OfType<ComboBox>().ElementAt(1);
            Click(formats); Check(formats.IsDropDownOpen && filter.Flyout!.IsOpen, "nested filter selection dismissed parent");
            Save("filter-choices-light.png");
            var choices = formats.GetVisualDescendants().OfType<Popup>().Single();
            SaveControl((Control)choices.Child!, "filter-choices-detail.png");
            Click((Control)formats.ContainerFromIndex(1)!); Wait(() => !model.IsLoading);
            Check(model.Filters.FormatIndex == 1 && filter.Flyout!.IsOpen, "native choice item input");
            model.Filters.RateIndex = 2;
            Wait(() => !model.IsLoading);
            Check(filter.Flyout!.IsOpen && model.Filters.ActiveCount == 2, "combined filters or parent lifetime");
            Save("albums-filters-light.png"); SaveMenu((Flyout)filter.Flyout!, "albums-filters-detail.png"); shell.IsDark = true; Save("albums-filters-dark.png");
            var favorite = panel.GetVisualDescendants().OfType<CheckBox>().First();
            Click(favorite); Wait(() => !model.IsLoading);
            Check(model.Filters.FavoriteOnly && favorite.IsChecked == true, "custom checkbox native input");
            shell.IsDark = false; SaveMenu((Flyout)filter.Flyout!, "albums-filters-checked.png");
            Click(favorite); Wait(() => !model.IsLoading); Check(!model.Filters.FavoriteOnly, "custom checkbox reversal");
            model.Filters.Year = "20"; Wait(() => !model.IsLoading);
            Check(model.Error.Length > 0 && model.ResidentAlbums == 0, "invalid year retained old actionable results");
            model.Filters.Reset(); Wait(() => !model.IsLoading && model.Total > 0); filter.Flyout.Hide();
            shell.Text.SetLanguage("en-US"); Check(model.SortLabel == "Random order", "localized sort label");
            shell.Text.SetLanguage("zh-CN"); shell.IsDark = false; shell.NavigateCommand.Execute("songs");
            Check(model.ResidentAlbums == 0, "album page release");
            var songs = window.GetVisualDescendants().OfType<SongsView>().Single();
            Wait(() => songs.Model is { IsLoading: false } && songs.Model.Items.Count > 0);
            var songSort = songs.GetVisualDescendants().OfType<SongSortControl>().Single().FindControl<Button>("SortButton")!;
            Click(songSort); Save("songs-sort-light.png"); SaveMenu((Flyout)songSort.Flyout!, "songs-sort-detail.png");
            var songContent = (Control)((Flyout)songSort.Flyout!).Content!;
            Click(songContent.GetVisualDescendants().OfType<ToggleButton>().Single());
            Check(songs.Model!.IsMultiSortEnabled, "multi-sort switch input");
            var artist = songContent.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("libraryOption") && button.DataContext is SongSortOption { Index: 1 });
            Click(artist); Wait(() => !songs.Model.IsLoading);
            Check(songSort.Flyout.IsOpen && songs.Model.SecondaryIndex == 2, "multi-sort order and menu lifetime");
            var artistDirection = songContent.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("libraryDirection") && button.DataContext is SongSortOption { Index: 1 });
            Click(artistDirection); Wait(() => !songs.Model.IsLoading);
            Check(songs.Model.SecondaryDescending, "multi-sort inline direction");
            Save("songs-sort-multi-light.png"); shell.IsDark = true; Save("songs-sort-dark.png");
            shell.NavigateCommand.Execute("home"); Check(songs.Model.Items.Count == 0, "song page release");
            Console.WriteLine("Avalonia: real menu input, sort directions, stable bounded random paging, nested filters, invalid-year recovery, localization and page release passed.");
        }
        finally
        {
            window.ReleaseLibraryPage(); window.Close(); shell.Settings.Dispose(); shell.ReleaseLocalization();
            var close = artwork.DisposeAsync().AsTask(); Wait(() => close.IsCompleted); close.GetAwaiter().GetResult();
            close = db.DisposeAsync().AsTask(); Wait(() => close.IsCompleted); close.GetAwaiter().GetResult();
        }
        void Save(string name)
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96)); bitmap.Render(window);
            bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
        void SaveMenu(Flyout flyout, string name)
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var presenter = ((Control)flyout.Content!).GetVisualAncestors().OfType<FlyoutPresenter>().First();
            SaveControl(presenter, name);
        }
        void SaveControl(Control target, string name)
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(target.Bounds.Width), (int)Math.Ceiling(target.Bounds.Height)), new Vector(96, 96));
            bitmap.Render(target); bitmap.Save(Path.Combine(output, name), PngBitmapEncoderOptions.Default);
        }
        void Click(Control target)
        {
            window.UpdateLayout(); var point = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
        }
    }
    private static void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }
    private static void Wait(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.Elapsed.TotalSeconds > 6) throw new TimeoutException("Browse UI wait exceeded 6 seconds."); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs();
    }
}
