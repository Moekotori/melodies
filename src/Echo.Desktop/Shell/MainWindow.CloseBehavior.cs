using Avalonia.Controls;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private void MinimizeToBackground()
    {
        if (_residency?.TryHideToTray(requirePreference: false) == true) return;
        ShowInTaskbar = true;
        WindowState = WindowState.Minimized;
    }
}
