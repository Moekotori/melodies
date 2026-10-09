using System.Text.Json;
using Echo.Contracts.Remote;

namespace Echo.Infrastructure.Remote;

// One indexed Subsonic song. The path is the song id, so a renamed album does not create a second library row.
internal sealed record SubsonicSongFacts(string Id, string Title, string Artist, string Album, string AlbumArtist, long Size, long ModifiedTicks,
    double Duration, string Codec, int? Track, int? Disc, int? Year, string Genre, int SampleRate, int BitDepth, string Cover)
{
    public RemoteIndexTrack ToTrack(string sourceId) => new(sourceId, SubsonicFileClient.SongPath(Id), Title, Artist, Album, Size, ModifiedTicks,
        Duration, Codec, AlbumArtist, Track, Disc, Year, Genre, SampleRate, BitDepth);
    public void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("id", Id); writer.WriteString("title", Title); writer.WriteString("artist", Artist);
        writer.WriteString("album", Album); writer.WriteString("albumArtist", AlbumArtist); writer.WriteNumber("size", Size);
        writer.WriteNumber("modified", ModifiedTicks); writer.WriteNumber("duration", Duration); writer.WriteString("codec", Codec);
        if (Track is int track) writer.WriteNumber("track", track);
        if (Disc is int disc) writer.WriteNumber("disc", disc);
        if (Year is int year) writer.WriteNumber("year", year);
        writer.WriteString("genre", Genre); writer.WriteNumber("sampleRate", SampleRate); writer.WriteNumber("bitDepth", BitDepth);
        writer.WriteString("cover", Cover); writer.WriteEndObject();
    }
    public static SubsonicSongFacts? From(JsonElement song, JsonElement album)
    {
        var id = song.TryGetProperty("id", out var raw) ? raw.ValueKind == JsonValueKind.Number ? raw.GetRawText() : raw.GetString() ?? "" : "";
        if (id.Length is 0 or > 256) return null;
        var title = Text(song, "title");
        if (title.Length == 0) title = Text(song, "name");
        if (title.Length == 0) title = id;
        var artist = Text(song, "artist");
        var albumName = Text(song, "album");
        if (albumName.Length == 0) albumName = Text(album, "name");
        if (albumName.Length == 0) albumName = Text(album, "title");
        var suffix = Text(song, "suffix");
        if (suffix.Length == 0) suffix = Text(song, "codec");
        var codec = suffix.Length == 0 ? "AUDIO" : suffix.ToUpperInvariant();
        var cover = Text(song, "cover");
        if (cover.Length == 0) cover = Text(song, "coverArt");
        if (cover.Length == 0) cover = Text(album, "coverArt");
        var modified = DateTimeOffset.TryParse(Text(song, "created").Length == 0 ? Text(song, "modified") : Text(song, "created"), out var date) ? date.UtcDateTime.Ticks : Number(song, "modified") ?? 0;
        return new(id, title, artist, albumName, ReadAlbumArtist(song, album), Number(song, "size") ?? 0, modified, Number(song, "duration") ?? 0, codec,
            Small(song, "track", 999), Small(song, "disc", 99) ?? Small(song, "discNumber", 99), Small(song, "year", 9999), ReadGenre(song, album),
            Rate(song, "sampleRate") ?? Rate(song, "samplingRate") ?? 0, Rate(song, "bitDepth") is int depth && depth is > 0 and <= 32 ? depth : 0, cover);
    }
    private static string ReadAlbumArtist(JsonElement song, JsonElement album)
    {
        var display = Text(song, "displayAlbumArtist");
        if (display.Length > 0) return display;
        var plain = Text(song, "albumArtist");
        if (plain.Length > 0) return plain;
        var names = new List<string>();
        if (song.TryGetProperty("albumArtists", out var references))
        {
            IEnumerable<JsonElement> items = references.ValueKind switch
            {
                JsonValueKind.Array => references.EnumerateArray(),
                JsonValueKind.Object => [references],
                _ => []
            };
            foreach (var item in items.Take(8))
            {
                var name = Text(item, "name");
                if (name.Length > 0) names.Add(name);
            }
        }
        if (names.Count > 0) return string.Join(" & ", names);
        return Text(album, "artist");
    }
    private static string ReadGenre(JsonElement song, JsonElement album)
    {
        var legacy = Text(song, "genre");
        if (legacy.Length > 0) return legacy;
        if (song.TryGetProperty("genres", out var genres))
        {
            IEnumerable<JsonElement> items = genres.ValueKind switch
            {
                JsonValueKind.Array => genres.EnumerateArray(),
                JsonValueKind.Object => [genres],
                _ => []
            };
            foreach (var item in items) { var name = Text(item, "name"); if (name.Length > 0) return name; }
        }
        return Text(album, "genre");
    }
    private static string Text(JsonElement item, string key)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String) return "";
        var text = value.GetString() ?? "";
        return text.Length > 512 ? text[..512] : text.Trim();
    }
    private static long? Number(JsonElement item, string key)
        => item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) && value.TryGetInt64(out var number) && number >= 0 ? number : null;
    private static int? Small(JsonElement item, string key, int max)
    {
        var number = Number(item, key);
        return number is >= 1 and var value && value <= max ? (int)value : null;
    }
    private static int? Rate(JsonElement item, string key)
    {
        var number = Number(item, key);
        return number is > 0 and <= 384000 ? (int)number : null;
    }
}
