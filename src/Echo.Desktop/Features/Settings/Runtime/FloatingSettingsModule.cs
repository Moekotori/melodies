using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class FloatingSettingsModule
{
    public FloatingSettingsModule(SettingsFeatureRuntime runtime, ShellViewModel shell)
    {
        runtime.RegisterChange("desktop-lyrics", (value, _) => { shell.Floating.LyricsEnabled = value.Enabled; return Task.CompletedTask; });
        runtime.RegisterAction("desktop-lyrics-position", (_, _) => { shell.Floating.ResetPositionCommand.Execute(null); return Task.CompletedTask; });
        runtime.RegisterAction("mini-player", (action, _) =>
        {
            if (action == "重置位置") shell.Floating.ResetPositionCommand.Execute(null);
            else shell.Floating.MiniEnabled = true;
            return Task.CompletedTask;
        });
    }
}
