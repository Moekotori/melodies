using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Playback;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel
{
    [ObservableProperty] private string signalIconState = "disconnected";
    private static string ResolveSignalIcon(PlaybackSnapshot state, PlaybackOutputState output)
    {
        if (!output.HasConfirmedDevice || state.Status is not (PlaybackStatus.Playing or PlaybackStatus.Paused)) return "disconnected";
        if (Flag(state.Processing, "dspActive") || Flag(state.Output, "dspActive")) return "dsp";
        var candidate = Flag(state.Processing, "bitPerfectCandidate") || Flag(state.Output, "bitPerfectCandidate");
        return candidate && string.IsNullOrEmpty(state.Error) && !Flag(state.Processing, "resampling") &&
            !Flag(state.Output, "resampling") && !Flag(state.Processing, "sampleRateMismatch") &&
            !Flag(state.Output, "sampleRateMismatch") ? "bit-perfect" : "modified";
    }
    private static bool Flag(JsonElement? value, string name) => value is { ValueKind: JsonValueKind.Object } obj &&
        obj.TryGetProperty(name, out var flag) && flag.ValueKind == JsonValueKind.True;
}
