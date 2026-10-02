using Echo.Contracts.Settings;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class PerformanceSettingsModule : IDisposable
{
    private static readonly string[] Lightweight = ["low-spec-mode", "low-load-playback", "low-load-playback-enhancements"];
    private readonly ShellViewModel shell;
    private readonly Action<string>? scanProfile;
    public PerformanceSettingsModule(SettingsFeatureRuntime runtime, ShellViewModel shell, Action<string>? scanProfile)
    {
        this.shell = shell; this.scanProfile = scanProfile;
        foreach (var id in Lightweight) runtime.RegisterChange(id, (value, _) =>
        {
            shell.Motion.LightweightMode = Lightweight.Any(key => key == id ? value.Enabled : shell.FeaturePreferences.Read(key, new()).Enabled);
            return Task.CompletedTask;
        });
        runtime.RegisterChange("renderer-frame-rate", (value, _) =>
        {
            shell.Motion.MaximumFrameRate = value.Selection switch { "默认（30 FPS）" => 30, "60 FPS" => 60, _ => throw new ArgumentException("帧率不受支持。") };
            return Task.CompletedTask;
        });
        if (scanProfile is not null) runtime.RegisterChange("scan-performance", (value, _) => { scanProfile(value.Selection); return Task.CompletedTask; });
        shell.FeaturePreferences.Changed += Restore;
    }
    private void Restore(string id)
    {
        if (id.Length != 0) return;
        shell.Motion.LightweightMode = Lightweight.Any(key => shell.FeaturePreferences.Read(key, new()).Enabled);
        shell.Motion.MaximumFrameRate = shell.FeaturePreferences.Read("renderer-frame-rate", new(false, "60 FPS")).Selection == "默认（30 FPS）" ? 30 : 60;
        var saved = shell.FeaturePreferences.Read("scan-performance", new(false, "均衡")).Selection;
        scanProfile?.Invoke(saved is "低占用" or "均衡" or "高性能" or "极速" ? saved : "均衡");
    }
    public void Dispose() => shell.FeaturePreferences.Changed -= Restore;
}
