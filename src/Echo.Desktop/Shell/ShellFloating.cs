using Echo.Desktop.Features.Floating;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    public FloatingWindowsViewModel Floating { get; private set; } = null!;
}
