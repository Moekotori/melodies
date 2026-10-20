using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Playback;
using static HeadphoneCatalogChecks;

internal static class HeadphoneReceiptChecks
{
    internal static async Task RunAsync()
    {
        var native = new ClampingHost();
        await using var session = new PlaybackSessionCoordinator(native);
        using var request = JsonDocument.Parse("[{\"enabled\":true,\"preampDb\":-30,\"bands\":[{\"enabled\":true,\"frequencyHz\":1000,\"gainDb\":20,\"q\":1,\"filterType\":\"peaking\"}]}]");
        try { await session.DspAsync("headphoneCorrection.setState", request.RootElement, true); throw new Exception("clamping host was accepted"); }
        catch (IOException) { }
        Check(native.Bypassed, "mismatched native parameters must bypass the correction");
        var retained = await session.GetRetainedDspRequestAsync("headphoneCorrection.setState");
        Check(retained is null, "failed native receipt must not enter recovery state");
        Console.WriteLine("PASS clamping host rejected, correction bypassed, no false recovery receipt");
    }

    private sealed class ClampingHost : INativeAudio
    {
        internal bool Bypassed;
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            if (method != "headphoneCorrection.setState" || parameters is not { } value) throw new Exception("unexpected fake host command");
            var state = JsonNode.Parse(value[0].GetRawText())!.AsObject();
            if (state["enabled"]?.ToString() == "false") Bypassed = true;
            else state["preampDb"] = -12;
            using var json = JsonDocument.Parse(state.ToJsonString()); return Task.FromResult(json.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
