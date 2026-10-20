using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Motion;
using Echo.Desktop.Features.Songs;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Search;

public partial class LibrarySearchView : UserControl, IDisposable
{
    private readonly ShellViewModel? shell;
    private readonly DetailNavigator? navigator;
    private readonly MotionPanes panes = new();
    private SongsViewModel? playing;
    private bool closed;
    public LibrarySearchViewModel? Model { get; }
    public LibrarySearchView() => InitializeComponent();
    public LibrarySearchView(ShellViewModel shell) : this()
    {
        this.shell = shell; Model = new(shell); DataContext = Model;
        navigator = new(shell); Details.Content = navigator;
        navigator.Back += CloseDetails;
        AttachedToVisualTree += (_, _) => FocusSearch(); DetachedFromVisualTree += (_, _) => Dispose();
    }
    public void FocusSearch() => SearchField.Focus();
    private void OnSearchKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Enter) { _ = Model?.LoadAsync(); args.Handled = true; } }
    private void OnClear(object? sender, RoutedEventArgs args) { if (Model is not null) Model.Search = ""; FocusSearch(); }
    private void OnOpenClick(object? sender, RoutedEventArgs args) => Open((sender as Control)?.DataContext as LibrarySearchHit);
    private void OnOpen(object? sender, TappedEventArgs args)
    {
        if (args.Source is not Avalonia.Visual visual) return;
        var ancestors = visual.GetSelfAndVisualAncestors().ToList();
        if (ancestors.OfType<Button>().Any() || ancestors.OfType<ScrollBar>().Any()) return;
        if (ancestors.OfType<ListBoxItem>().FirstOrDefault()?.DataContext is LibrarySearchHit hit) { HitList.SelectedItem = hit; Open(hit); }
    }
    private void OnResultsKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Enter) { Open(HitList.SelectedItem as LibrarySearchHit); args.Handled = true; } }
    private async void Open(LibrarySearchHit? hit)
    {
        if (hit is null || closed || shell is null) return;
        if (hit.Target is TrackRow track && shell.TrackLibrary is { } library)
        {
            playing ??= new(library, shell.Player, shell.Text);
            playing.Items = [track]; playing.Selected = track;
            await playing.PlayCommand.ExecuteAsync(null);
            if (!closed && Model is not null) Model.Error = playing.Error;
        }
        else
        {
            if (hit.Target is AlbumRow album) navigator?.OpenAlbum(album);
            if (hit.Target is ArtistRow artist) navigator?.OpenArtist(artist);
            _ = panes.Show(Details, Results, MotionDirection.Forward);
        }
    }
    private async void CloseDetails()
    {
        var settled = panes.Show(Results, Details, MotionDirection.Back);
        FocusSearch();
        if (await settled && !closed) navigator?.Release();
    }
    public void SetWindowVisible(bool visible) => navigator?.SetWindowVisible(visible);
    public void Dispose() { if (closed) return; closed = true; panes.Dispose(); Model?.Dispose(); playing?.Dispose(); navigator?.Dispose(); }
}
