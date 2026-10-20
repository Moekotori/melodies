namespace Echo.Desktop.Features.Onboarding;

public sealed record OperationGuideStep(string Id, string? Target = null, string? Route = null, string? Action = null);

// Matches ECHO's operation tour, using only capabilities present in this client.
public static class OperationGuideSteps
{
    public static IReadOnlyList<OperationGuideStep> All { get; } = [
        new("welcome"),
        new("songs", "TrackList", "songs"),
        new("import", "LibraryButton", "songs", "import"),
        new("play", "TrackList", "songs"),
        new("controls", "Transport", "home"),
        new("progress", "SeekSlider", "home"),
        new("queue", "QueueButton", "home", "queue"),
        new("queue-list", "QueueTracks", "queue"),
        new("lyrics", "LyricsButton", "home", "lyrics"),
        new("visuals", "LyricsGuideButton", "home", "visuals"),
        new("lyrics-style", "LyricStyleInput", "home", "visuals"),
        new("lyrics-background", "LyricBackgroundInput", "home", "background"),
        new("settings", "SettingsGuideButton", "home", "settings"),
        new("categories", "CategoryNavigation", "settings"),
        new("finish")
    ];
}
