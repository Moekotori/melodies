using System.Text.Json;
using Echo.Playback;

internal static class AlphaProtocolChecks
{
    internal static async Task RunAsync()
    {
        var track = AlphaPlanningChecks.Track();
        var plan = AutomixPlanner.Create(AlphaPlanningChecks.Input(track, track));
        await using var native = new Host(plan.PlanId);
        Assert.True(await AutomixNativePreparation.PrepareAsync(native, plan, "next.wav"), "confirmed Alpha host arms");
        Assert.True(native.Request!.Value[0].GetProperty("plan").GetProperty("alphaDsp").GetProperty("style").GetString() == "plain_blend", "production request reaches native host");
        native.AlphaSupported = false; native.Calls.Clear();
        await Throws(() => AutomixNativePreparation.PrepareAsync(native, plan, "next.wav"), "Alpha");
        Assert.True(native.Calls.SequenceEqual(["automix.prepare", "automix.cancel"]), "old host preparation is cancelled before reporting fallback");
        native.RejectCancel = true;
        await Throws(() => AutomixNativePreparation.PrepareAsync(native, plan, "next.wav"), "取消");
        native.AlphaSupported = true; native.PlanId = "wrong-plan";
        await Throws(() => AutomixNativePreparation.PrepareAsync(native, plan, "next.wav"), "不匹配");
        native.PlanId = plan.PlanId; native.RejectCancel = false;
        await AutomixNativePreparation.CancelAsync(native, plan.PlanId);
        Assert.True(native.Calls.Last() == "automix.cancel", "disable awaits authoritative cancellation");
    }

    private static async Task Throws(Func<Task> action, string message)
    {
        try { await action(); }
        catch (IOException error) when (error.Message.Contains(message, StringComparison.Ordinal)) { return; }
        throw new Exception("Expected protocol rejection: " + message);
    }

    private sealed class Host(string planId) : INativeAudio
    {
        internal string PlanId = planId;
        internal bool AlphaSupported = true, RejectCancel;
        internal readonly List<string> Calls = [];
        internal JsonElement? Request;
        public event Action<string, JsonElement>? Notification { add { } remove { } }
        public event Action<Exception>? Failed { add { } remove { } }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<JsonElement> CallAsync(string method, JsonElement? parameters = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(method); Request = parameters?.Clone();
            var result = new System.Text.Json.Nodes.JsonObject
            {
                ["acknowledged"] = method != "automix.cancel" || !RejectCancel,
                ["state"] = method == "automix.cancel" ? "idle" : "armed",
                ["alphaDspAcknowledged"] = AlphaSupported, ["planId"] = PlanId,
            };
            using var json = JsonDocument.Parse(result.ToJsonString());
            return Task.FromResult(json.RootElement.Clone());
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
