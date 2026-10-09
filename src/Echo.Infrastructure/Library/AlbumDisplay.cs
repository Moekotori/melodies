namespace Echo.Infrastructure.Library;

// One album shows one artist: the shared album artist, otherwise the shared track artist.
// Distinct performers stay on their own tracks and the album displays Various Artists.
internal static class AlbumDisplay
{
    public const string Various = "Various Artists";

    public static string Refresh(string predicate) => $"""
        UPDATE albums SET
          title=coalesce((
            SELECT coalesce(nullif(trim(album),''),'未知专辑') FROM tracks
            WHERE album_key=albums.id AND missing=0
            GROUP BY lower(trim(album))
            ORDER BY count(*) DESC, coalesce(nullif(trim(album),''),'未知专辑') LIMIT 1), title),
          artist=coalesce((
            SELECT CASE
              WHEN EXISTS(SELECT 1 FROM tracks WHERE album_key=albums.id AND missing=0 AND length(trim(album_artist))>0)
                THEN (SELECT trim(album_artist) FROM tracks WHERE album_key=albums.id AND missing=0 AND length(trim(album_artist))>0
                      GROUP BY lower(trim(album_artist)) ORDER BY count(*) DESC, trim(album_artist) LIMIT 1)
              WHEN (SELECT count(DISTINCT lower(trim(artist))) FROM tracks
                    WHERE album_key=albums.id AND missing=0 AND length(trim(artist))>0)<=1
                THEN coalesce((SELECT trim(artist) FROM tracks WHERE album_key=albums.id AND missing=0 AND length(trim(artist))>0
                      GROUP BY lower(trim(artist)) ORDER BY count(*) DESC, trim(artist) LIMIT 1),'未知艺术家')
              ELSE '{Various}'
            END), artist)
        WHERE {predicate};
        UPDATE albums SET sort_key=upper(title), artist_key=upper(artist) WHERE {predicate};
        """;
}
