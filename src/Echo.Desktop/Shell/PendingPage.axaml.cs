using Avalonia.Controls;

namespace Echo.Desktop.Shell;

public partial class PendingPage : UserControl
{
    public PendingPage() => InitializeComponent();
    // Capture identity: the outgoing page must not turn into the destination page.
    public PendingPage(string title, string route) : this()
    { PageTitle.Text = title; PageIcon.Kind = route; }
}
