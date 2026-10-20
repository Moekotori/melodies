using System.Runtime.InteropServices;
using System.Text.Json;
using Echo.Playback;

namespace Echo.Desktop.Features.Player;

// Retains only the last presented path inputs, never a queue or a history of IPC frames.
internal sealed class SignalPathPresentationState
{
    private PlaybackSnapshot? playback;
    private PlaybackOutputState? output;
    private Guid generation;
    private bool available;
    private string format = "";
    private int sampleRate, bitDepth;

    public void Invalidate() => playback = null;

    public bool Update(PlaybackStateSnapshot frame, string nextFormat, int nextRate, int nextDepth)
    {
        var next = frame.Playback;
        var changed = playback is null || frame.Generation != generation || frame.IsAvailable != available
            || nextFormat != format || nextRate != sampleRate || nextDepth != bitDepth
            || !SamePlayback(playback, next) || !SameOutput(output!, frame.Output);
        if (!changed) return false;
        playback = next; output = frame.Output; generation = frame.Generation; available = frame.IsAvailable;
        format = nextFormat; sampleRate = nextRate; bitDepth = nextDepth;
        return true;
    }

    private static bool SamePlayback(PlaybackSnapshot a, PlaybackSnapshot b) => a.OperationId == b.OperationId
        && a.Status == b.Status && a.Volume == b.Volume && a.Error == b.Error
        && a.Current?.ItemId == b.Current?.ItemId && a.Current?.FilePath == b.Current?.FilePath
        && a.Current?.Source?.Kind == b.Current?.Source?.Kind
        && Metadata(a.Current, "codec") == Metadata(b.Current, "codec")
        && Metadata(a.Current, "sampleRate") == Metadata(b.Current, "sampleRate")
        && Metadata(a.Current, "duration") == Metadata(b.Current, "duration")
        && SameJson(a.Processing, b.Processing);

    private static bool SameOutput(PlaybackOutputState a, PlaybackOutputState b) =>
        a.HasConfirmedDevice == b.HasConfirmedDevice && a.DeviceName == b.DeviceName && a.Backend == b.Backend
        && SameJson(a.Confirmed, b.Confirmed) && SameJson(a.Requested, b.Requested);

    private static string? Metadata(QueueTrack? track, string key) => track?.Metadata?.GetValueOrDefault(key);
    private static bool SameJson(JsonElement? a, JsonElement? b) => a.HasValue == b.HasValue && (!a.HasValue ||
        JsonMarshal.GetRawUtf8Value(a.Value).SequenceEqual(JsonMarshal.GetRawUtf8Value(b!.Value)));
}
