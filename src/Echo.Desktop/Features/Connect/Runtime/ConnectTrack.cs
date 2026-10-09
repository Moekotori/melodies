using Echo.Desktop.Features.Player;
using Echo.Playback;

namespace Echo.Desktop.Features.Connect.Runtime;

internal readonly record struct ConnectTrackInfo(string Location, string Title, string Artist, string Album, string Mime, bool LocalFile);

internal static class ConnectTrack
{
    public static ConnectTrackInfo? Current(PlayerViewModel? player)
    {
        var track = player?.Session.Snapshot.Current;
        if (track is null) return null;
        var source = AudioSource.Playable(track);
        var title = Text(track, "title") ?? Path.GetFileNameWithoutExtension(source.Kind == "local" ? source.Uri : track.FilePath);
        var artist = Text(track, "artist") ?? "";
        var album = Text(track, "album") ?? "";
        if (source.Kind == "local")
        {
            source.Validate();
            return new(source.Uri, title, artist, album, Mime(source.Uri), true);
        }
        if (source.Kind == "http" && ConnectAddress.IsHttp(source.Uri))
            return new(source.Uri, title, artist, album, "audio/mpeg", false);
        return null;
    }

    public static string Mime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".wav" => "audio/wav",
        ".m4a" or ".mp4" or ".aac" or ".alac" => "audio/mp4",
        ".ogg" or ".opus" => "audio/ogg",
        ".aif" or ".aiff" => "audio/aiff",
        ".wma" => "audio/x-ms-wma",
        _ => "application/octet-stream"
    };

    public static string Didl(string url, string mime, string title, string artist)
        => "<?xml version=\"1.0\"?>"
           + "<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">"
           + "<item id=\"1\" parentID=\"0\" restricted=\"1\"><dc:title>" + ConnectXml.Escape(title) + "</dc:title>"
           + "<upnp:artist>" + ConnectXml.Escape(artist) + "</upnp:artist><upnp:class>object.item.audioItem.musicTrack</upnp:class>"
           + "<res protocolInfo=\"http-get:*:" + ConnectXml.Escape(mime) + ":*\">" + ConnectXml.Escape(url) + "</res></item></DIDL-Lite>";

    private static string? Text(QueueTrack track, string key)
    {
        var value = track.Metadata?.GetValueOrDefault(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
