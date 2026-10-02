using Avalonia.Controls;

namespace Echo.Desktop.Shell;

// Rebuildable main-window visuals. The window and playback owner outlive this tree.
public partial class MainWindowSurface : UserControl
{
    public MainWindowSurface() => InitializeComponent();
}
