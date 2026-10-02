using System.Diagnostics;
using Echo.Contracts.Tasks;
using Echo.Playback;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel
{
    private long presentedPerformanceOperation;
    public void RefreshPerformance(PlaybackStateSnapshot? frame = null)
    {
        if (Volatile.Read(ref detached) != 0) return;
        performancePresentationTime = 0;
        PresentPerformance(frame ?? session.State);
    }
    private void PresentPerformance(PlaybackStateSnapshot state)
    {
        var now = Stopwatch.GetTimestamp();
        if (state.Playback.OperationId == presentedPerformanceOperation && state.Playback.Status == PlaybackStatus.Playing && performancePresentationTime != 0
            && Stopwatch.GetElapsedTime(performancePresentationTime, now).TotalMilliseconds < 500) return;
        performancePresentationTime = now;
        presentedPerformanceOperation = state.Playback.OperationId;
        string Unknown() => Text.Translate("未知");
        var metrics = state.Performance;
        var fresh = metrics.Timestamp > 0 && Stopwatch.GetElapsedTime(metrics.Timestamp, now).TotalMilliseconds <= 2000;
        var buffer = fresh && metrics.BufferedMilliseconds is { } milliseconds ? $"{milliseconds:0} ms"
            : fresh && metrics.BufferedFrames is { } frames ? $"{frames:N0} frames" : Unknown();
        string Stage(ProcessingStagePerformance? stage) => stage?.Speed is { } speed
            && Stopwatch.GetElapsedTime(stage.Timestamp, now).TotalMilliseconds <= 2500 ? $"{speed:0.0}x" : Unknown();
        var policy = workAdmission?.Snapshot;
        var pressure = policy?.Pressure switch
        {
            PlaybackWorkPressure.Protected => Text.Translate("播放保护：新后台工作等待"),
            PlaybackWorkPressure.Reduced => Text.Translate("后台已限速"),
            PlaybackWorkPressure.Normal => Text.Translate("后台正常"),
            _ => Unknown()
        };
        var count = metrics.HostUnderrunCallbacks?.ToString("N0") ?? Unknown();
        var reason = policy?.Reason switch
        {
            PlaybackWorkReason.PreparingPlayback => Text.Translate("正在准备播放"),
            PlaybackWorkReason.TelemetryUnknown => Text.Translate("遥测不足或过期"),
            PlaybackWorkReason.LowBuffer => Text.Translate("PCM 缓冲偏低"),
            PlaybackWorkReason.Underrun => Text.Translate("检测到新的宿主欠载"),
            PlaybackWorkReason.SlowStage => Text.Translate("处理阶段余量不足"),
            PlaybackWorkReason.Recovering => Text.Translate("等待稳定恢复"),
            _ => ""
        };
        PerformanceText = $"{Text.Translate("PCM 缓冲")}：{buffer}\n{Text.Translate("宿主累计欠载")}：{count}\n" +
            $"{Text.Translate("SRC 单阶段倍率")}：{Stage(metrics.Src)} · SDM：{Stage(metrics.Sdm)}\n" +
            $"{Text.Translate("完整 DSP 倍率 / 回调长尾")}：{Unknown()}\n{pressure}" +
            (reason.Length == 0 ? "" : $"（{reason}）") +
            (policy is null ? "" : $" · metadata {policy.ActiveMetadata}/{policy.MetadataConcurrency}");
    }
}
