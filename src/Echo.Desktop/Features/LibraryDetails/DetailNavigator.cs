using Avalonia.Controls;
using Avalonia.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Features.Genres;
using Echo.Desktop.Shell;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.LibraryDetails;

// History retains only bounded DTOs; every replacement cancels and releases the previous visual tree.
public sealed class DetailNavigator : MotionPresenter, IDisposable
{
    private readonly ShellViewModel shell;
    public DetailNavigator(ShellViewModel shell)
    {
        this.shell = shell;
        ContentRetired += content => (content as IDisposable)?.Dispose();
    }
    private readonly List<object> _history = [];
    private CancellationTokenSource? _resolve;
    private bool _closed, _visible = true;
    public event Action? Back;
    public event Action<string, bool, int>? PersonalStateChanged;
    public void OpenAlbum(AlbumRow album) => Open(album);
    public void OpenArtist(ArtistRow artist) => Open(artist);
    public void OpenGenre(GenreRow genre) => Open(genre);
    private void Open(object target)
    {
        if (_closed) return;
        _resolve?.Cancel();
        if (_history.Count == 16) _history.RemoveAt(0);
        _history.Add(target); Show(target);
    }
    private void Show(object target)
    {
        if (target is AlbumRow album)
        {
            var view = new AlbumDetailView(album, shell.AlbumLibrary!, shell.AlbumArtwork!, shell.Player, shell.DetailResources, shell.Text);
            view.Back += GoBack; view.ArtistRequested += name => _ = ResolveArtistAsync(name);
            view.GenreRequested += name => _ = ResolveGenreAsync(name);
            view.AlbumRequested += OpenAlbum;
            view.PersonalStateChanged += (id, favorite, rating) =>
            {
                for (var i = 0; i < _history.Count; i++)
                    if (_history[i] is AlbumRow old && old.Id == id) _history[i] = old with { Favorite = favorite, Rating = rating };
                PersonalStateChanged?.Invoke(id, favorite, rating);
            };
            Content = view;
        }
        else if (target is GenreRow genre)
        {
            var view = new GenreDetailView(genre, shell); view.Back += GoBack; view.OpenAlbum += OpenAlbum;
            Content = view; view.SetWindowVisible(_visible);
        }
        else if (target is ArtistRow artist)
        {
            var view = new ArtistDetailView(artist, shell); view.Back += GoBack; view.OpenAlbum += OpenAlbum;
            Content = view; view.SetWindowVisible(_visible);
        }
    }
    private async Task ResolveArtistAsync(string name)
    {
        if (shell.ArtistLibrary is not { } library || _closed) return;
        _resolve?.Cancel(); using var request = new CancellationTokenSource(); _resolve = request;
        try
        {
            var page = await library.GetArtistsAsync(new(Search: name[..Math.Min(128, name.Length)], Grouping: ArtistGrouping.AlbumArtist), request.Token);
            if (_closed || request.IsCancellationRequested) return;
            var canonical = name.Normalize(System.Text.NormalizationForm.FormKC).Trim();
            var artist = page.Items.FirstOrDefault(item => item.Name.Normalize(System.Text.NormalizationForm.FormKC).Trim().Equals(canonical, StringComparison.OrdinalIgnoreCase));
            if (artist is not null) OpenArtist(artist); else shell.Notice = "没有找到对应的专辑艺术家，请刷新曲库。";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) shell.Notice = "艺术家读取失败，请重试。"; }
        finally { if (ReferenceEquals(_resolve, request)) _resolve = null; }
    }
    private async Task ResolveGenreAsync(string name)
    {
        if (shell.GenreLibrary is not { } library || _closed) return;
        _resolve?.Cancel(); using var request = new CancellationTokenSource(); _resolve = request;
        try
        {
            var page = await library.GetGenresAsync(new(Search: name[..Math.Min(128, name.Length)]), request.Token);
            if (_closed || request.IsCancellationRequested) return;
            var canonical = name.Normalize(System.Text.NormalizationForm.FormKC).Trim();
            var genre = page.Items.FirstOrDefault(item => item.Name.Normalize(System.Text.NormalizationForm.FormKC).Trim().Equals(canonical, StringComparison.OrdinalIgnoreCase));
            if (genre is not null) OpenGenre(genre); else shell.Notice = "没有找到对应的流派，请刷新曲库。";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!_closed) shell.Notice = "流派读取失败，请重试。"; }
        finally { if (ReferenceEquals(_resolve, request)) _resolve = null; }
    }
    public void GoBack()
    {
        _resolve?.Cancel();
        if (_history.Count > 0) _history.RemoveAt(_history.Count - 1);
        if (_history.Count > 0) Show(_history[^1]);
        else { Content = null; Back?.Invoke(); }
    }
    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (!args.Handled && args.Key == Key.Escape && _history.Count > 0) { GoBack(); args.Handled = true; }
    }
    public void SetWindowVisible(bool visible)
    {
        _visible = visible;
        if (Content is GenreDetailView genre) genre.SetWindowVisible(visible);
        if (Content is ArtistDetailView artist) artist.SetWindowVisible(visible);
        if (Content is AlbumDetailView album) album.SetWindowVisible(visible);
    }
    public void Dispose() { if (_closed) return; _closed = true; _resolve?.Cancel(); Content = null; Settle(); _history.Clear(); Back = null; PersonalStateChanged = null; }
}
