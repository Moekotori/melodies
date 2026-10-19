using System.Text.Json;
using Echo.Core.Contracts;

namespace Echo.Output.Host;

internal sealed partial class OutputWorker
{
    private readonly Dictionary<string, OutputEvent> telemetry = new(StringComparer.Ordinal);
    private void NativeEvent(string method, JsonElement parameters)
    {
        OutputEvent value;
        lock (gate)
        {
            if (releasing || generation == Guid.Empty) return;
            value = new(generation, method, parameters.ValueKind == JsonValueKind.Undefined ? CoreJson.Empty : parameters);
            if (method is "audio.position" or "host.performance" or "audio.levelMeter" or "levelMeter.level") {
                telemetry[method] = value;
                if (telemetrySignal.CurrentCount == 0) telemetrySignal.Release();
                return;
            }
        }
        // Ordered playback facts use the reliable channel; overflow faults the connection instead of dropping EOF/drain.
        Notice?.Invoke(new("event", Method: "output.native", Payload: OutputJson.Value(value)));
    }
    private void NativeFailed(Exception _)
    {
        OutputEvent value;
        lock (gate)
        {
            if (releasing || !started) return;
            started = false; telemetry.Clear(); value = new(generation, "failed", CoreJson.Empty);
        }
        Notice?.Invoke(new("event", Method: "output.failed", Payload: OutputJson.Value(value)));
    }
}
