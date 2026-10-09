using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Echo.Playback;

public sealed partial class PlaybackSessionCoordinator
{
    private const double AutomixUrgentLeadSeconds = 2;
    private bool automixEnabled;
    private bool outputFrameKnown;
    private long outputFrame;
    private long outputFrameOperation;
    private string? automixArmedKey;
    private string? automixPendingKey;
    private int automixGeneration;
    private CancellationTokenSource? automixAnalysis;
    private string? armedPlanId;
    private readonly Dictionary<string, double> durations = new(StringComparer.Ordinal);

    public Task SetAutomixEnabledAsync(bool enabled, CancellationToken token = default) => Command(async () =>
    {
        automixEnabled = enabled;
        CancelAutomixAnalysis();
        if (!enabled)
        {
            if (armedPlanId is { } plan)
            {
                armedPlanId = null;
                try { await native.CallAsync("automix.cancel", Params(new JsonArray(new JsonObject { ["planId"] = plan }))).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { }
            }
            return;
        }
        await TryArmAutomixAsync().ConfigureAwait(false);
    }, token, affectsPlayback: false);

    private async Task TryArmAutomixAsync()
    {
        if (!automixEnabled || snapshot.Status != PlaybackStatus.Playing || segmentLoop is not null
            || Math.Abs(snapshot.PlaybackRate - 1) > 1e-6 || OutputFormat() != "pcm" || StageActive("sdm")) return;
        if (!outputFrameKnown || outputFrameOperation != snapshot.OperationId || snapshot.Current is not { } current || !PlainLocal(current)) return;
        if (snapshot.DurationSeconds <= 0) return;
        var prepared = await PrepareNativeQueueAsync(ProjectNativeQueue(queue, current.ItemId), current.ItemId, repeat).ConfigureAwait(false);
        if (prepared.Repeat == "one") return;
        var index = Array.FindIndex(prepared.Items, x => x.ItemId == current.ItemId);
        var followers = index < 0 ? [] : GaplessSuccessors(prepared.Items, index, prepared.Repeat == "all").Take(2).ToArray();
        var next = followers.Length == 0 ? null : followers[0];
        if (next is null || !PlainLocal(next) || IsDsdPath(current.FilePath) || IsDsdPath(next.FilePath)) return;
        var key = string.Create(CultureInfo.InvariantCulture, $"{revision}:{current.ItemId}:{next.ItemId}:{snapshot.OperationId}");
        if (automixArmedKey == key) return;
        durations[current.ItemId] = snapshot.DurationSeconds;
        var nextDuration = durations.TryGetValue(next.ItemId, out var known) ? known : await ReadDurationAsync(next).ConfigureAwait(false);
        if (nextDuration is > 0) durations[next.ItemId] = nextDuration.Value;
        var cachedCurrent = AutomixAnalyzer.TryGet(current.FilePath);
        var cachedNext = AutomixAnalyzer.TryGet(next.FilePath);
        var ready = cachedCurrent is not null && cachedNext is not null;
        var remaining = snapshot.DurationSeconds - snapshot.PositionSeconds;
        var urgent = remaining <= Math.Max(2, 16) + AutomixUrgentLeadSeconds;
        if (ready || urgent)
        {
            CancelAutomixAnalysis();
            automixArmedKey = key;
            await SendAutomixAsync(current, next,
                ready ? cachedCurrent! : AutomixAnalysis.Unavailable(snapshot.DurationSeconds, "analysis_deadline"),
                ready ? cachedNext! : AutomixAnalysis.Unavailable(nextDuration ?? 0, "analysis_deadline")).ConfigureAwait(false);
            return;
        }
        if (automixPendingKey == key) return;
        automixPendingKey = key;
        var generation = ++automixGeneration;
        automixAnalysis?.Cancel();
        automixAnalysis?.Dispose();
        automixAnalysis = new CancellationTokenSource();
        var token = automixAnalysis.Token;
        var fromId = current.ItemId;
        var toId = next.ItemId;
        var currentPath = current.FilePath;
        var nextPath = next.FilePath;
        var currentDuration = snapshot.DurationSeconds;
        var incomingDuration = nextDuration ?? 0;
        var warm = followers.Length > 1 && PlainLocal(followers[1]) && !IsDsdPath(followers[1].FilePath) ? followers[1] : null;
        var warmPath = warm?.FilePath;
        var warmDuration = warm is null ? 0 : durations.TryGetValue(warm.ItemId, out var warmKnown) ? warmKnown : 0;
        _ = Task.Run(async () =>
        {
            try
            {
                var left = await AutomixAnalyzer.AnalyzeAsync(currentPath, currentDuration, token).ConfigureAwait(false);
                var right = await AutomixAnalyzer.AnalyzeAsync(nextPath, incomingDuration, token).ConfigureAwait(false);
                if (!lane.Post(() => FinishAutomixAsync(generation, key, fromId, toId, left, right))) return;
                if (warmPath is not null && warmDuration > 0)
                    _ = AutomixAnalyzer.AnalyzeAsync(warmPath, warmDuration, token);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { }
        }, token);
    }

    private async Task FinishAutomixAsync(int generation, string key, string fromId, string toId, AutomixAnalysis currentAnalysis, AutomixAnalysis nextAnalysis)
    {
        if (generation != automixGeneration || automixArmedKey == key || !automixEnabled || snapshot.Status != PlaybackStatus.Playing) return;
        if (snapshot.Current is not { } current || current.ItemId != fromId) return;
        if (!key.EndsWith(string.Create(CultureInfo.InvariantCulture, $":{snapshot.OperationId}"), StringComparison.Ordinal)) return;
        var prepared = await PrepareNativeQueueAsync(ProjectNativeQueue(queue, fromId), fromId, repeat).ConfigureAwait(false);
        var index = Array.FindIndex(prepared.Items, item => item.ItemId == fromId);
        var next = index < 0 ? null : GaplessSuccessors(prepared.Items, index, prepared.Repeat == "all").FirstOrDefault();
        if (next is null || next.ItemId != toId || !PlainLocal(next)) return;
        automixArmedKey = key;
        automixPendingKey = null;
        await SendAutomixAsync(current, next, currentAnalysis, nextAnalysis).ConfigureAwait(false);
    }

    private async Task SendAutomixAsync(QueueTrack current, QueueTrack next, AutomixAnalysis currentAnalysis, AutomixAnalysis nextAnalysis)
    {
        var plan = AutomixPlanner.Create(new AutomixPlanInput
        {
            QueueRevision = revision, FromItemId = current.ItemId, FromTrackId = current.TrackId,
            ToItemId = next.ItemId, ToTrackId = next.TrackId, MixSampleRate = snapshot.SampleRate,
            CurrentOutputFrame = outputFrame, PositionSeconds = snapshot.PositionSeconds,
            Current = currentAnalysis, Next = nextAnalysis, PlaybackRate = snapshot.PlaybackRate,
            CurrentIsDsd = IsDsdPath(current.FilePath), NextIsDsd = IsDsdPath(next.FilePath),
        });
        if (plan.Mode == "gapless_fallback") { armedPlanId = null; return; }
        var request = new JsonObject
        {
            ["plan"] = plan.ToJson(),
            ["nextSource"] = new JsonObject { ["kind"] = "local", ["uri"] = next.FilePath },
        };
        try
        {
            var result = await native.CallAsync("automix.prepare", Params(new JsonArray(request))).ConfigureAwait(false);
            armedPlanId = result.Flag("acknowledged") && result.Text("state") == "armed" ? result.Text("planId") : null;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { armedPlanId = null; }
    }

    private void CancelAutomixAnalysis()
    {
        automixGeneration++;
        automixArmedKey = null;
        automixPendingKey = null;
        automixAnalysis?.Cancel();
        automixAnalysis?.Dispose();
        automixAnalysis = null;
    }

    private async Task<double?> ReadDurationAsync(QueueTrack track)
    {
        if (sourceProbe is null) return null;
        try
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            return await sourceProbe.ReadDurationAsync(track.Source ?? AudioSource.Local(track.FilePath), budget.Token).ConfigureAwait(false);
        }
        catch (Exception) { return null; }
    }

    private async Task AdoptAutomixAsync(JsonElement value)
    {
        if (value.Long("queueRevision") != revision || value.Text("fromItemId") != snapshot.Current?.ItemId) return;
        var next = queue.FirstOrDefault(x => x.ItemId == value.Text("toItemId") && x.TrackId == value.Text("toTrackId"));
        var operation = value.Long("operationId");
        if (next is null || operation <= 0) return;
        var previous = snapshot.Current;
        CompleteTransition(previous, next);
        positionBase = value.Number("sourcePositionSeconds");
        outputFrame = value.Long("outputFrame");
        outputFrameOperation = operation;
        outputFrameKnown = true;
        CancelAutomixAnalysis();
        armedPlanId = null;
        var duration = durations.TryGetValue(next.ItemId, out var known) ? known : 0;
        Publish(snapshot with { Current = next, OperationId = operation, Status = PlaybackStatus.Playing,
            PositionSeconds = positionBase, DurationSeconds = duration, SampleRate = snapshot.SampleRate });
        if (previous is not null && (previous.QueuedNext || previous.RemoveAfterPlay) && previous.ItemId != next.ItemId)
            await SendQueueAsync(queue, next.ItemId, repeat).ConfigureAwait(false);
        await TryArmAutomixAsync().ConfigureAwait(false);
    }

    private void NoteAutomixFallback(JsonElement value)
    {
        if (value.Text("planId") == armedPlanId) armedPlanId = null;
    }
}
