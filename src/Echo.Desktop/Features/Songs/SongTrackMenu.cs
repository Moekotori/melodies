using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Desktop.Controls;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Songs;

// One native menu per page. It captures a stable row and cancels submenu reads on close.
internal sealed class SongTrackMenu : IDisposable
{
    private readonly SongsView owner;
    private readonly ShellViewModel shell;
    private readonly SongsViewModel model;
    private readonly SongMenuActions actions;
    private CancellationTokenSource? request;
    private CancellationTokenSource? playlistRequest;
    private TrackRow? target;
    private readonly MenuItem placeholder = new() { IsEnabled = false };
    private bool closed;
    public ContextMenu Menu { get; } = new() { MinWidth = 260, MaxWidth = 300 };

    public SongTrackMenu(SongsView owner, ShellViewModel shell, SongsViewModel model)
    {
        this.owner = owner; this.shell = shell; this.model = model; actions = new(owner, shell, model);
        Menu.Classes.Add("songTrackMenu");
        placeholder.Header = shell.Text["query.actions"]; Menu.ItemsSource = new[] { placeholder };
        Menu.Opening += (_, args) =>
        {
            if (closed || target is not { } row || !Select(row)) { args.Cancel = true; return; }
            request?.Cancel(); request?.Dispose(); request = new();
            Menu.ItemsSource = Build(row, request.Token);
        };
        Menu.Closed += (_, _) => { CancelReads(); target = null; Menu.ItemsSource = closed ? null : new[] { placeholder }; };
        model.PropertyChanged += OnModelChanged;
    }

    public void Prepare(TrackRow? row) { if (Menu.IsOpen) Menu.Close(); target = row; Menu.Placement = PlacementMode.Pointer; }
    public void Show(TrackRow row, Control anchor)
    { if (closed) return; Menu.Close(); target = row; Menu.Placement = PlacementMode.BottomEdgeAlignedRight; Menu.Open(anchor); }
    private bool Select(TrackRow row)
    {
        if (model.IsLoading || model.IsActing || !model.Items.Any(item => item.Id == row.Id)) return false;
        model.Selected = model.Items.First(item => item.Id == row.Id); return true;
    }
    private Control[] Build(TrackRow row, CancellationToken token)
    {
        var heading = new StackPanel { Spacing = 3 };
        var title = new TextBlock { Text = row.Title, FontSize = 14, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        title.Bind(TextBlock.ForegroundProperty, owner.GetResourceObservable("EchoHeading"));
        var subtitle = new TextBlock { Text = row.Artist, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
        subtitle.Bind(TextBlock.ForegroundProperty, owner.GetResourceObservable("EchoMuted"));
        heading.Children.Add(title); heading.Children.Add(subtitle);
        var header = new MenuItem { Header = heading, IsEnabled = false }; header.Classes.Add("songMenuHeader");
        var playlists = Item("songs.menu.playlist", "queue", null, shell.PlaylistLibrary is not null);
        playlists.Name = "SongAddToPlaylist";
        playlists.ItemsSource = new[] { new MenuItem { Header = shell.Text["songs.menu.loading"], IsEnabled = false } };
        var started = false;
        playlists.SubmenuOpened += (_, _) => { if (!started) { started = true; _ = LoadPlaylistsAsync(playlists, row, 0, token); } };
        return
        [
            header, new Separator(),
            Command("query.playTrack", "play-solid", model.PlayCommand, row, shell.Player is not null, "SongMenuPlay"),
            Command("query.nextUp", "skip-forward", model.PlayNextCommand, row, shell.Player is not null, "SongMenuNext"),
            Command("query.add", "queue", model.AddCommand, row, shell.Player is not null, "SongMenuAppend"),
            new Separator(),
            Command(row.Favorite ? "songs.menu.unfavorite" : "query.favorite", row.Favorite ? "heart-filled" : "heart", model.FavoriteCommand, row,
                shell.AlbumLibrary is IAlbumPersonalState, "SongMenuFavorite"),
            playlists, new Separator(),
            Item("songs.menu.album", "album", () => actions.OpenAlbumAsync(row), shell.AlbumLibrary is IHomeLibrary && row.Album.Length > 0),
            Item("songs.menu.artist", "user", () => actions.OpenArtistAsync(row), shell.ArtistLibrary is not null && row.Artist.Length > 0),
            Item("songs.menu.info", "info", () => actions.ShowInfoAsync(row), true, "SongMenuInfo"),
            new Separator(),
            Item("songs.menu.folder", "folder", () => actions.OpenFolderAsync(row), Path.IsPathFullyQualified(SongMenuActions.FilePath(row))),
            Item("songs.menu.copy", "copy", () => actions.CopyPathAsync(row), TopLevel.GetTopLevel(owner)?.Clipboard is not null, "SongMenuCopy")
        ];
    }
    private MenuItem Command(string label, string icon, IAsyncRelayCommand command, TrackRow row, bool available, string name)
        => Item(label, icon, async () => { if (Select(row)) await command.ExecuteAsync(null); }, available, name);
    private MenuItem Item(string key, string icon, Func<Task>? action, bool available = true, string? name = null)
    {
        var glyph = new GlyphIcon { Kind = icon, Width = 16, Height = 16 };
        glyph.Bind(GlyphIcon.StrokeProperty, owner.GetResourceObservable("EchoMuted"));
        var item = new MenuItem { Header = shell.Text[key], Icon = glyph, IsEnabled = available, Name = name };
        item.Classes.Add("songMenuAction");
        if (action is not null) item.Click += async (_, _) =>
        { try { await action(); } catch (OperationCanceledException) { } catch (Exception) { if (!closed) model.Error = shell.Text["songs.menu.failed"]; } };
        return item;
    }
    private async Task LoadPlaylistsAsync(MenuItem parent, TrackRow row, int offset, CancellationToken token)
    {
        if (shell.PlaylistLibrary is not { } library || closed || token.IsCancellationRequested) return;
        playlistRequest?.Cancel(); playlistRequest?.Dispose();
        var current = CancellationTokenSource.CreateLinkedTokenSource(token); playlistRequest = current;
        try
        {
            var page = await library.GetPlaylistsAsync(new(offset, 20), current.Token);
            if (closed || current.IsCancellationRequested || !Menu.IsOpen) return;
            var items = page.Items.Select(playlist =>
            {
                var item = Item("songs.menu.playlist", "queue", () => actions.AddToPlaylistAsync(row, playlist), true);
                item.Header = playlist.Name; item.Name = "SongPlaylistTarget"; return (Control)item;
            }).ToList();
            if (items.Count == 0) items.Add(new MenuItem { Header = shell.Text["songs.menu.noPlaylists"], IsEnabled = false });
            if (offset > 0 || page.Total > offset + page.Items.Count)
            {
                items.Add(new Separator());
                if (offset > 0) items.Add(PageItem("query.previous", offset - 20));
                if (page.Total > offset + page.Items.Count) items.Add(PageItem("query.next", offset + 20));
            }
            items.Add(new Separator()); items.Add(Item("songs.menu.newPlaylist", "queue", () => actions.CreatePlaylistAsync(row), true, "SongNewPlaylist"));
            parent.ItemsSource = items;
            MenuItem PageItem(string label, int next)
            {
                var item = Item(label, next < offset ? "chevron-left" : "chevron-right", () => LoadPlaylistsAsync(parent, row, next, token));
                item.StaysOpenOnClick = true; return item;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!closed && !current.IsCancellationRequested && Menu.IsOpen)
                parent.ItemsSource = new[] { Item("songs.menu.retry", "refresh", () => LoadPlaylistsAsync(parent, row, offset, token)) };
        }
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(SongsViewModel.Items) || (args.PropertyName == nameof(SongsViewModel.IsActing) && model.IsActing)) Menu.Close(); }
    private void CancelReads()
    { playlistRequest?.Cancel(); playlistRequest?.Dispose(); playlistRequest = null; request?.Cancel(); request?.Dispose(); request = null; }
    public void Dispose()
    { if (closed) return; closed = true; model.PropertyChanged -= OnModelChanged; Menu.Close(); CancelReads(); Menu.ItemsSource = null; actions.Dispose(); }
}
