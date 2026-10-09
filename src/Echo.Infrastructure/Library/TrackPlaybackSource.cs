using Echo.Contracts.Library;
using Echo.Playback;

namespace Echo.Infrastructure.Library;

public static class TrackPlaybackSource
{
    public static AudioSource Audio(TrackRow row) => row.Source?.Kind == "remote" || row.Path.StartsWith("remote://", StringComparison.Ordinal)
        ? new("remote", row.Path.StartsWith("remote://", StringComparison.Ordinal) ? row.Path : row.Source!.AudioPath)
        : row.Source is { Kind: "cue" } cue
        ? AudioSource.Local(cue.AudioPath, cue.StartSeconds, cue.EndSeconds)
        : AudioSource.Local(row.Source?.AudioPath ?? row.Path);
}
