using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Echo.Application.Playback;
using Echo.Contracts.Settings;
using Echo.Desktop.Shell;
using Echo.Platform.Windows;
using Echo.Playback;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class PlaybackSettingsModule : IDisposable
{
    private readonly ShellViewModel shell;
    private readonly IPlaybackRuntime playback;
    private readonly SettingsDialogs dialogs;
    private readonly CancellationTokenSource lifetime = new();
    private bool disposed;
    public PlaybackSettingsModule(SettingsFeatureRuntime runtime, ShellViewModel shell, IPlaybackRuntime playback, SettingsDialogs dialogs)
    {
        this.shell = shell; this.playback = playback; this.dialogs = dialogs;
        runtime.RegisterChange("fixed-volume", (value, token) => playback.LocalZone.Session.SetVolumeLockedAsync(value.Enabled, token));
        runtime.RegisterChange("speed-mode", (value, token) => playback.LocalZone.Session.SetPlaybackRateModeAsync(
            value.Selection switch { "Nightcore" => "nightcore", "Doubletime" => "speed", _ => throw new ArgumentException("无效的变速模式。") }, token));
        runtime.RegisterChange("auto-play-on-startup", (_, _) => Task.CompletedTask); // Consumed once in StartupAsync.
        runtime.RegisterChange("gapless-playback", (value, token) => playback.LocalZone.Session.SetAlbumGaplessEnabledAsync(value.Enabled, token));
        runtime.RegisterChange("automix", (value, token) => playback.LocalZone.Session.SetAutomixEnabledAsync(value.Enabled, token));
        runtime.RegisterChange("wdm-ks", async (value, _) =>
        { shell.Player!.AudioSettings.ShowKernelStreaming = value.Enabled; await shell.Player.AudioSettings.RefreshCommand.ExecuteAsync(null); });
        if (OperatingSystem.IsWindows()) runtime.RegisterChange("prevent-sleep-while-playing", (_, _) => Task.CompletedTask);
        runtime.RegisterChange("auto-output", (value, token) => value.Enabled ? SetDefaultOutputAsync(token)
            : Configuration()["deviceId"]?.GetValue<string>() is { Length: > 0 } ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException("请先选择具体输出设备，再关闭自动选择。")));
        runtime.RegisterChange("output-mode", (value, token) =>
        {
            var key = value.Selection switch { "安全模式" or "共享模式（推荐）" => "shared", "独占模式" => "exclusive", "ASIO" or "ASIO 独占" => "asio", _ => throw new ArgumentException("不支持的输出模式。") };
            return ApplyOutputModeAsync(key, token);
        });
        runtime.RegisterAction("output-device", async (_, token) =>
        {
            var session = playback.LocalZone.Session;
            var config = Configuration(); var mode = config["outputMode"]?.GetValue<string>() ?? "shared";
            var devices = (await session.GetOutputDevicesAsync(mode, token)).Take(256).ToArray();
            if (devices.Length == 0) throw new IOException("当前模式未找到可用设备，请先选择其他输出模式。");
            var labels = devices.Select((device, index) => $"{index + 1}. {device.Name}").ToArray();
            var values = await dialogs.EditAsync("选择输出设备", "选择当前输出模式的设备。确认后由播放会话应用，并以原生回执显示结果。",
                new SettingsField("device", "输出设备", labels[0], Choices: labels));
            var selected = devices[Array.IndexOf(labels, values["device"])];
            await session.ConfigureOutputAsync(new AudioOutputConfiguration(mode, selected, 2,
                config["sampleRate"]?.GetValue<int>() ?? 48000, config["bufferSize"]?.GetValue<int>() ?? 1024, 0, "pcm", 176400), token);
            shell.FeaturePreferences.Confirm("auto-output", new(selected.IsDefault));
            shell.Settings.Feedback = "输出设备配置已确认；实际设备状态见信号路径。";
        });
        runtime.RegisterAction("audio-status", (_, _) => dialogs.ShowAsync("当前输出状态", shell.Player!.SignalPath + "\n\n" + shell.Player.PerformanceText));
        runtime.RegisterAction("audio-recover", async (action, token) =>
        {
            if (action != "重启播放") throw new NotSupportedException("请在 Windows 系统工具中重启音频服务；ECHO 的重试只重建自身音频宿主。");
            await playback.LocalZone.Session.RecoverAsync(token);
        });
        shell.FeaturePreferences.Changed += PreferencesChanged;
        playback.LocalZone.Session.VolumeLockChanged += LockChanged;
    }
    private JsonObject Configuration() => playback.LocalZone.Session.RequestedOutput is { } saved
        ? JsonNode.Parse(saved.GetRawText())!.AsObject() : new() { ["outputMode"] = "shared", ["deviceId"] = "", ["sampleRate"] = 48000, ["channels"] = 2, ["bufferSize"] = 1024 };

    private async Task ApplyOutputModeAsync(string key, CancellationToken token)
    {
        var session = playback.LocalZone.Session;
        if (!(await session.GetOutputModesAsync(token)).Any(mode => mode.Key == key))
            throw new NotSupportedException(key == "asio" ? "ASIO 独占只在 Windows 音频宿主上可用。" : "当前宿主或平台不支持所选输出模式。");
        var configuration = Configuration();
        configuration["outputMode"] = key;
        configuration["deviceId"] = "";
        configuration["deviceName"] = "";
        configuration["deviceIndex"] = -1;
        var format = configuration["processing"] is JsonObject processing ? processing["outputFormat"]?.GetValue<string>() ?? "pcm" : "pcm";
        if (key == "shared" || format == "dsd-native-raw" && key != "asio" || format == "dop24le" && key is not ("asio" or "exclusive"))
        {
            format = "pcm";
            configuration["processing"] = new JsonObject { ["outputFormat"] = "pcm" };
        }
        var channels = configuration["channels"]?.GetValue<int>() ?? 2;
        if (key != "asio" || format != "pcm" || channels is < 2 or > 8) configuration["channels"] = 2;
        if (key != "shared")
        {
            var devices = (await session.GetOutputDevicesAsync(key, token)).Take(256).ToArray();
            var selected = devices.FirstOrDefault(device => device.IsDefault) ?? devices.FirstOrDefault()
                ?? throw new IOException(key == "asio" ? "没有找到 ASIO 驱动。请安装声卡的 ASIO 驱动后再使用 ASIO 独占。" : "当前模式未找到可用设备，请连接设备后重试。");
            configuration["deviceId"] = selected.Id;
            configuration["deviceName"] = selected.Name;
            configuration["deviceIndex"] = selected.Index;
        }
        using var json = JsonDocument.Parse(configuration.ToJsonString());
        await session.ConfigureOutputAsync(json.RootElement, token);
    }
    private async Task SetDefaultOutputAsync(CancellationToken token)
    {
        var config = Configuration(); config["outputMode"] = "shared"; config["deviceId"] = ""; config["deviceName"] = ""; config["deviceIndex"] = -1;
        config["processing"] = new JsonObject { ["outputFormat"] = "pcm" };
        using var json = JsonDocument.Parse(config.ToJsonString());
        await playback.LocalZone.Session.ConfigureOutputAsync(json.RootElement, token);
    }
    public async Task StartupAsync()
    {
        await playback.StartAsync(lifetime.Token);
        await RestoreAsync();

    }
    private void PreferencesChanged(string id) { }
    private async Task RestoreAsync()
    {
        try
        {
            await playback.LocalZone.Session.SetVolumeLockedAsync(shell.FeaturePreferences.Read("fixed-volume", new()).Enabled, lifetime.Token);
            await playback.LocalZone.Session.SetAlbumGaplessEnabledAsync(shell.FeaturePreferences.Read("gapless-playback", new()).Enabled, lifetime.Token);
            await playback.LocalZone.Session.SetAutomixEnabledAsync(shell.FeaturePreferences.Read("automix", new()).Enabled, lifetime.Token);
            shell.Player!.AudioSettings.ShowKernelStreaming = shell.FeaturePreferences.Read("wdm-ks", new()).Enabled;
            if (shell.FeaturePreferences.Snapshot().ContainsKey("speed-mode"))
                await playback.LocalZone.Session.SetPlaybackRateModeAsync(shell.FeaturePreferences.Read("speed-mode", new(false, "Doubletime")).Selection == "Nightcore" ? "nightcore" : "speed", lifetime.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) shell.Settings.Feedback = error.Message; }
    }
    private void LockChanged(bool value) => Dispatcher.UIThread.Post(() =>
    { if (!disposed) shell.FeaturePreferences.Confirm("fixed-volume", new(value)); });
    public void Dispose()
    {
        disposed = true; lifetime.Cancel();
        shell.FeaturePreferences.Changed -= PreferencesChanged;
        playback.LocalZone.Session.VolumeLockChanged -= LockChanged;
    }
}
