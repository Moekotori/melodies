using System.Globalization;
using Echo.Application.Playback;
using Echo.Desktop.Features.Player;
using Echo.Playback;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class PlaybackToolsSettingsModule
{
    public PlaybackToolsSettingsModule(SettingsFeatureRuntime runtime, PlayerToolsViewModel tools,
        SleepTimerService timer, PlaybackSessionCoordinator session)
    {
        runtime.RegisterChange("sleep-timer-minutes", (value, _) =>
        {
            if (!double.TryParse(value.Selection, CultureInfo.InvariantCulture, out var minutes) || !double.IsFinite(minutes) || minutes is < 1 or > 720)
                throw new ArgumentException("定时时长应为 1–720 分钟。");
            tools.Minutes = minutes;
            return Task.CompletedTask;
        });
        runtime.RegisterChange("sleep-timer-action", (value, _) =>
        {
            tools.ActionIndex = value.Selection switch
                { "暂停" => 0, "停止" => 1, "退出应用" => 2, _ => throw new ArgumentException("无效的定时动作。") };
            return Task.CompletedTask;
        });
        runtime.RegisterAction("sleep-timer", (action, token) => action == "取消定时"
            ? timer.CancelAsync(token) : timer.StartAsync(tools.Minutes,
                (SleepTimerAction)tools.ActionIndex, token));
        runtime.RegisterAction("segment-loop", (_, token) => session.ClearSegmentLoopAsync(token));
    }
}
