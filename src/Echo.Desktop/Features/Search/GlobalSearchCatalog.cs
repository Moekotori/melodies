using Echo.Desktop.Features.Settings.Catalog;
using Echo.Desktop.Features.Settings.Models;
using Echo.Desktop.Shell;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Search;

public sealed record SearchPageTarget(string Route);
public sealed record GlobalSearchHit(string Kind, string Title, string Description, string Icon, object Target)
{
    public string ArtworkPath => Target switch { TrackRow track => track.Path, AlbumRow album => album.SamplePath,
        PlaylistRow playlist => playlist.SamplePath ?? "", _ => "" };
    public bool HasArtwork => ArtworkPath.Length > 0;
}

// Only the fixed, existing navigation/settings catalog. No library residency or second index.
internal static class GlobalSearchCatalog
{
    internal static IEnumerable<GlobalSearchHit> Match(ShellViewModel shell, string query)
    {
        var text = shell.Text;
        var pages = shell.LibraryItems.Concat(shell.PlaybackItems)
            .Where(item => item.Id != "playlists" || shell.PlaylistLibrary is not null)
            .Select(item => (item.Id, item.Label, item.SourceLabel))
            .Concat(new[] { ("settings", text.Translate("设置"), "设置 settings"),
                ("audio-settings", text.Translate("音频设置"), "音频 输出 audio output"),
                ("lyrics", text.Translate("歌词"), "歌词 lyrics") });
        foreach (var (id, title, aliases) in pages)
        {
            if (id == "lyrics" && shell.LyricsSource is null || id == "listening") continue;
            if (!Matches(query, title + " " + aliases + " " + id)) continue;
            var icon = id switch { "songs" => "music", "albums" => "disc", "artists" => "user",
                "folders" => "folder", "queue" or "playlists" => "queue", "history" => "history",
                "settings" or "audio-settings" => "settings", "lyrics" => "captions", _ => "home" };
            yield return new(text["globalSearch.page"], title, text["globalSearch.openPage"], icon, new SearchPageTarget(id));
        }
        var count = 0;
        foreach (var category in SettingsCatalog.Categories)
        foreach (var section in category.Sections)
        foreach (var row in section.Rows)
        {
            var title = text.Translate(row.Title); var categoryTitle = text.Translate(category.Title);
            var searchText = string.Join(' ', new[] { title, categoryTitle, text.Translate(row.Help), row.Id,
                text.Translate(section.Title), row.Title }.Concat((row.Options ?? []).Select(text.Translate)));
            if (!Matches(query, searchText)) continue;
            yield return new(text["globalSearch.setting"], title, categoryTitle, "settings",
                new SettingsSearchResult(category.Id, row.Id, title, categoryTitle));
            if (++count == 16) yield break;
        }
    }

    private static bool Matches(string query, string value) => query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .All(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
