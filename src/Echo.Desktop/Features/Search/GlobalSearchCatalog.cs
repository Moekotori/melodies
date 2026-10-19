using Echo.Desktop.Features.Settings.Catalog;
using Echo.Desktop.Features.Settings.Models;
using Echo.Desktop.Shell;
using Echo.Contracts.Library;
using Echo.Playback;

namespace Echo.Desktop.Features.Search;

public sealed record SearchPageTarget(string Route);
public sealed record SearchQueryTarget(string Route, string Query);
public sealed record SearchFavoriteTarget(TrackRow Track);
public sealed record GlobalSearchHit(string Kind, string Title, string Description, string Icon, object Target)
{
    public string SectionTitle { get; init; } = "";
    public bool StartsSection => SectionTitle.Length > 0;
    public bool HasDescription => Description.Length > 0 && Target is not SearchPageTarget && Target is not SettingsSearchResult;
    public string Context => HasDescription ? "—  " + Description : "";
    public string Key => Target switch
    {
        QueueTrack queue => "queue:" + queue.ItemId,
        TrackRow track => "track:" + track.Id, AlbumRow album => "album:" + album.Id,
        ArtistRow artist => "artist:" + artist.Id, GenreRow genre => "genre:" + genre.Id,
        PlaylistRow playlist => "playlist:" + playlist.Id, FolderRow folder => "folder:" + folder.Path,
        HistoryRecord history => "history:" + history.Id, SearchFavoriteTarget liked => "liked:" + liked.Track.Id,
        SearchPageTarget page => "page:" + page.Route, SearchQueryTarget query => query.Route + ":" + query.Query,
        SettingsSearchResult setting => "setting:" + setting.CategoryId + ":" + setting.RowId,
        _ => Title
    };
    public string ArtworkPath => Target switch { TrackRow track => track.Path, AlbumRow album => album.SamplePath,
        PlaylistRow playlist => playlist.SamplePath ?? "", FolderRow folder => folder.SamplePath ?? "",
        QueueTrack queue => queue.FilePath, HistoryRecord history => history.Track.Path, SearchFavoriteTarget liked => liked.Track.Path, _ => "" };
    public bool HasArtwork => ArtworkPath.Length > 0;
}

// Only the fixed, existing navigation/settings catalog. No library residency or second index.
internal static class GlobalSearchCatalog
{
    internal static IEnumerable<GlobalSearchHit> Pages(ShellViewModel shell)
    {
        var text = shell.Text;
        var pages = shell.LibraryItems.Concat(shell.SourceItems).Concat(shell.PlaybackItems)
            .Where(item => item.Id != "playlists" || shell.PlaylistLibrary is not null)
            .Select(item => (item.Id, item.Label, item.SourceLabel))
            .Concat(new[] { ("settings", text.Translate("设置"), "设置 settings"),
                ("audio-settings", text.Translate("音频设置"), "音频 输出 audio output"),
                ("lyrics", text.Translate("歌词"), "歌词 lyrics") });
        foreach (var (id, title, aliases) in pages)
        {
            if (id == "lyrics" && shell.LyricsSource is null || id is "listening" or "workshop" ||
                id == "remote" && shell.RemoteSources is null) continue;
            var icon = id switch { "songs" => "music", "albums" => "disc", "artists" => "user",
                "folders" => "folder", "genres" => "disc", "album-artists" => "user",
                "remote" => "database", "connect" => "link", "radio" => "waves", "podcasts" => "headphones", "dsp" => "sliders", "queue" or "playlists" => "queue", "history" => "history",
                "settings" or "audio-settings" => "settings", "lyrics" => "captions", _ => "lyric-home" };
            yield return new(text["globalSearch.page"], title, aliases, icon, new SearchPageTarget(id));
        }
    }

    internal static IEnumerable<GlobalSearchHit> Match(ShellViewModel shell, string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var hit in Pages(shell).Where(hit => Matches(terms, hit.Title + " " + hit.Description + " " + ((SearchPageTarget)hit.Target).Route)))
            yield return hit;
        var text = shell.Text;
        var count = 0;
        foreach (var category in SettingsCatalog.Categories)
        foreach (var section in category.Sections)
        foreach (var row in section.Rows)
        {
            var title = text.Translate(row.Title); var categoryTitle = text.Translate(category.Title);
            var searchText = string.Join(' ', new[] { title, categoryTitle, text.Translate(row.Help), row.Id,
                text.Translate(section.Title), row.Title }.Concat((row.Options ?? []).Select(text.Translate)));
            if (!Matches(terms, searchText)) continue;
            yield return new(text["globalSearch.setting"], title, categoryTitle, "settings",
                new SettingsSearchResult(category.Id, row.Id, title, categoryTitle));
            if (++count == 16) yield break;
        }
    }

    private static bool Matches(string[] terms, string value) => terms
        .All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
