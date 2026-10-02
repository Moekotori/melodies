using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Remote;

public partial class RemoteProviderPicker : UserControl
{
    public RemoteProviderPicker() => InitializeComponent();
    public void FocusFirst() => this.GetVisualDescendants().OfType<Button>().FirstOrDefault(x => x.Tag as string == "subsonic")?.Focus();
}
