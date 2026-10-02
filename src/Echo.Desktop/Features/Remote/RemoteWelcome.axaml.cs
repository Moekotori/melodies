using Avalonia.Controls;

namespace Echo.Desktop.Features.Remote;

public partial class RemoteWelcome : UserControl
{
    public RemoteWelcome() => InitializeComponent();
    public void FocusConnect() => ConnectButton.Focus();
}
