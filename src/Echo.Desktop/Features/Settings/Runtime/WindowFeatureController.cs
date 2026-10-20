using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Echo.Contracts.Settings;
using Echo.Desktop.Shell;
using Echo.Platform.MacOS;
using Echo.Platform.Windows;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class WindowFeatureController : IDisposable
{
    private readonly MainWindow _window;
    private readonly ShellViewModel _shell;
    private readonly SettingsDialogs _dialogs;


    private bool _disposed;
    public WindowFeatureController(MainWindow window, ShellViewModel shell, SettingsFeatureRuntime runtime, SettingsDialogs dialogs)
    {
        _window = window; _shell = shell; _dialogs = dialogs;
        runtime.RegisterChange("close-to-tray", (value, _) =>
        {
            shell.CloseToTray = value.Enabled; return Task.CompletedTask;
        });
        runtime.RegisterChange("ultra-light-mode", (value, _) => { shell.UltraLightEnabled = value.Enabled; return Task.CompletedTask; });
        runtime.RegisterChange("launch-at-login", (value, token) =>
        {
            var path = Environment.ProcessPath ?? throw new IOException("无法定位 melodies 可执行文件。");
            return OperatingSystem.IsMacOS()
                ? MacOsLoginItem.SetEnabledAsync(value.Enabled, path, token)
                : WindowsDesktopFeatures.SetStartupAsync(value.Enabled, path, token);
        });
        runtime.RegisterChange("touch-keyboard", (value, token) => WindowsDesktopFeatures.SetTouchKeyboardAsync(value.Enabled, token));
        runtime.RegisterChange("accessibility-eye-comfort", (value, _) => { SetComfort(value.Enabled); return Task.CompletedTask; });
        runtime.RegisterChange("accessibility-high-contrast", (value, _) => { SetContrast(value.Enabled); return Task.CompletedTask; });
        runtime.RegisterChange("accessibility-focus", (value, _) => { _window.Classes.Set("always-focus", value.Enabled); return Task.CompletedTask; });
        runtime.RegisterAction("first-run-wizard", (_, _) => { shell.OpenTutorialCommand.Execute(null); return Task.CompletedTask; });
        runtime.RegisterAction("operation-guide", (_, _) => { shell.OpenOperationGuideCommand.Execute(null); return Task.CompletedTask; });
        shell.FeaturePreferences.Changed += OnPreferencesChanged;
        shell.Theme.Changed += RefreshAccessibility;
    }
    private void OnPreferencesChanged(string id)
    {
        if (_disposed) return;
        if (id.Length == 0)
        {

            SetComfort(_shell.FeaturePreferences.Read("accessibility-eye-comfort", new()).Enabled);
            SetContrast(_shell.FeaturePreferences.Read("accessibility-high-contrast", new()).Enabled);
            _window.Classes.Set("always-focus", _shell.FeaturePreferences.Read("accessibility-focus", new()).Enabled);
        }
    }
    private void SetComfort(bool enabled)
    {
        if (enabled) _window.Resources["EchoPageBase"] = new SolidColorBrush(Color.Parse(_shell.IsDark ? "#181714" : "#f7f1e5"));
        else _window.Resources.Remove("EchoPageBase");
    }
    private void RefreshAccessibility()
    {
        SetComfort(_shell.FeaturePreferences.Read("accessibility-eye-comfort", new()).Enabled);
        SetContrast(_shell.FeaturePreferences.Read("accessibility-high-contrast", new()).Enabled);
    }
    private void SetContrast(bool enabled)
    {
        if (enabled)
        {
            _window.Resources["EchoText"] = _window.Resources["EchoHeading"] = _window.Resources["EchoMuted"] = _shell.IsDark ? Brushes.White : Brushes.Black;
            _window.Resources["EchoDivider"] = _shell.IsDark ? Brushes.White : Brushes.Black;
        }
        else foreach (var key in new[] { "EchoText", "EchoHeading", "EchoMuted", "EchoDivider" }) _window.Resources.Remove(key);
    }
    public void Dispose() { _disposed = true; _shell.FeaturePreferences.Changed -= OnPreferencesChanged; _shell.Theme.Changed -= RefreshAccessibility; }
}
