using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Features.Songs;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Search;

public partial class LibrarySearchView : UserControl, IDisposable
{
    private readonly ShellViewModel? shell;
    private readonly DetailNavigator? navigator;
    private SongsViewModel? playing;
    private bool closed;
    public LibrarySearchViewModel? Model { get; }
    public LibrarySearchView() => InitializeComponent();
    public LibrarySearchView(ShellViewModel shell) : this()
    {
        this.shell = shell; Model = new(shell); DataContext = Model;
        navigator = new(shell); Details.Content = navigator;
        navigator.Back += () => { Details.IsVisible = false; Results.IsVisible = true; FocusSearch(); };
        AttachedToVisualTree += (_, _) => FocusSearch(); DetachedFromVisualTree += (_, _) => Dispose();
    }
    public void FocusSearch() => SearchField.Focus();
    private void OnSearchKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Enter) { _ = Model?.LoadAsync(); args.Handled = true; } }
    private void OnClear(object? sender, RoutedEventArgs args) { if (Model is not null) Model.Search = ""; FocusSearch(); }
    private void OnOpenClick(object? sender, RoutedEventArgs args) => Open((sender as Control)?.DataContext as LibrarySearchHit);
    private void OnOpen(object? sender, TappedEventArgs args) => Open(HitList.SelectedItem as LibrarySearchHit);
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
            Results.IsVisible = false; Details.IsVisible = true;
        }
    }
    public void SetWindowVisible(bool visible) => navigator?.SetWindowVisible(visible);
    public void Dispose() { if (closed) return; closed = true; Model?.Dispose(); playing?.Dispose(); navigator?.Dispose(); }
}
