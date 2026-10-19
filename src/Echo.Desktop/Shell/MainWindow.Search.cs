using Avalonia.Input;
using Echo.Contracts.Library;
using Echo.Playback;
using Echo.Desktop.Features.Search;
using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private void OnGlobalSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Handled || _viewModel is null || _viewModel.Shortcuts.IsRecording) return;
        if (args.Key != Key.F4 || args.KeyModifiers != KeyModifiers.None) return;
        args.Handled = true;
        // Match the original host: F4 works in text fields and ignores auto-repeat.
        if (_shortcutKeysDown.Add(args.Key)) _viewModel.ToggleGlobalSearchCommand.Execute(null);
    }

    private void OpenGlobalResult(GlobalSearchHit hit) => OpenLibraryTarget(hit.Target);
    internal void OpenArtistSearch(string name)
    {
        if (_viewModel is not { } shell) return;
        shell.NavigateCommand.Execute("artists");
        if (_artistsPage is not null) _artistsPage.Model.Search = name[..Math.Min(128, name.Length)];
    }
    private async void OpenSearchTrack(TrackRow track, bool favoriteOnly = false)
    {
        var shell = _viewModel!;
        shell.NavigateCommand.Execute("songs");
        if (_songsPage?.Model is not { } songs) return;
        songs.Filters.Reset(); songs.Filters.FavoriteOnly = favoriteOnly;
        songs.Search = track.Title;
        await songs.LoadAsync();
        if (_songsPage?.Model == songs) songs.Selected = songs.Items.FirstOrDefault(item => item.Id == track.Id);
    }
    internal void OpenLibraryTarget(object target)
    {
        if (_viewModel is not { } shell) return;
        switch (target)
        {
            case TrackRow track:
                OpenSearchTrack(track); break;
            case SearchFavoriteTarget liked:
                OpenSearchTrack(liked.Track, favoriteOnly: true); break;
            case QueueTrack queue:
                shell.NavigateCommand.Execute("queue");
                if (shell.Player is { } player) player.Queue.Search = queue.Metadata?.GetValueOrDefault("title") ?? Path.GetFileNameWithoutExtension(queue.FilePath);
                break;
            case HistoryRecord history:
                shell.NavigateCommand.Execute("history");
                if (_historyPage?.Model is { } historyModel) { historyModel.RangeIndex = 0; historyModel.CountedOnly = false; historyModel.Search = history.Track.Title; }
                break;
            case FolderRow folder:
                shell.LastFolderPath = folder.Path;
                shell.NavigateCommand.Execute("folders");
                if (_foldersPage?.Model is { } folders)
                { folders.SelectedNode = null; folders.CurrentFolder = folder; _ = folders.LoadAsync(); }
                break;
            case SearchQueryTarget query:
                shell.NavigateCommand.Execute(query.Route);
                if (query.Route == "settings") shell.Settings.Query = query.Query;
                break;
            case AlbumRow album:
                shell.NavigateCommand.Execute("albums"); _albumsPage?.OpenDetail(album); break;
            case ArtistRow artist:
                shell.NavigateCommand.Execute("artists"); _artistsPage?.OpenDetail(artist); break;
            case GenreRow genre:
                shell.NavigateCommand.Execute("genres"); _genresPage?.OpenDetail(genre); break;
            case PlaylistRow playlist:
                shell.NavigateCommand.Execute("playlists"); _playlistsPage?.Model.OpenPlaylist(playlist); break;
            case SettingsSearchResult setting:
                shell.NavigateCommand.Execute("settings"); shell.Settings.OpenResultCommand.Execute(setting); break;
            case SearchPageTarget page:
                if (page.Route == "audio-settings") shell.OpenQuickSettingsCommand.Execute("audio");
                else shell.NavigateCommand.Execute(page.Route);
                break;
        }
    }
}
