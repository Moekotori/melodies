using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Playback.MediaTransport;

namespace Echo.Platform.Windows;

internal static class SmtcProtocol
{
    public static bool Ready(JsonElement message)
    {
        if (Text(message, "type") != "ready" || !message.TryGetProperty("protocolVersion", out var version)
            || !version.TryGetInt32(out var number) || number != 1
            || !message.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Object) return false;
        return new[] { "metadata", "timeline", "enabledActions", "seekCommands", "localArtwork", "playbackModes" }
            .All(key => capabilities.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True);
    }

    public static MediaTransportCommand? Command(JsonElement message)
    {
        if (Text(message, "type") != "command") return null;
        var name = Text(message, "command");
        if (name is "play" or "pause" or "stop" or "next" or "previous" or "setShuffle") return new(name);
        if (name == "setRepeat" && Text(message, "mode") is "none" or "track" or "list") return new(name, Mode: Text(message, "mode"));
        if (name == "seek" && message.TryGetProperty("positionSeconds", out var value) && value.TryGetDouble(out var position)
            && double.IsFinite(position) && position >= 0 && position <= 31_536_000) return new(name, position);
        return null;
    }

    public static string? Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    public static IEnumerable<string> Updates(MediaTransportState state, MediaTransportState? previous)
    {
        if (state.ItemId is null)
        {
            if (previous?.ItemId is not null) yield return Message(new() { ["type"] = "clear" });
            yield break;
        }
        if (previous is null || state.ItemId != previous.ItemId || state.Title != previous.Title || state.Artist != previous.Artist
            || state.Album != previous.Album || state.AlbumArtist != previous.AlbumArtist || state.CoverPath != previous.CoverPath)
            yield return Message(new() { ["type"] = "setMetadata", ["title"] = state.Title, ["artist"] = state.Artist,
                ["album"] = state.Album, ["albumArtist"] = state.AlbumArtist, ["coverPath"] = state.CoverPath });
        if (previous is null || state.Play != previous.Play || state.Pause != previous.Pause || state.Previous != previous.Previous
            || state.Next != previous.Next || state.Seek != previous.Seek)
            yield return Message(new() { ["type"] = "setEnabledActions", ["play"] = state.Play, ["pause"] = state.Pause,
                ["previous"] = state.Previous, ["next"] = state.Next, ["seek"] = state.Seek });
        // Restate modes after an unsupported shuffle request, without inventing a queue owner.
        yield return Message(new() { ["type"] = "setPlaybackModes", ["repeat"] = state.Repeat, ["shuffle"] = false });
        if (previous is null || state.Status != previous.Status)
            yield return Message(new() { ["type"] = "setPlaybackState", ["state"] = state.Status });
        yield return Message(new() { ["type"] = "setTimeline", ["positionSeconds"] = state.PositionSeconds, ["durationSeconds"] = state.DurationSeconds });
    }

    private static string Message(JsonObject message)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) message.WriteTo(writer);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
