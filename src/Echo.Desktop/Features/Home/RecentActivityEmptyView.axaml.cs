using Avalonia.Controls;

namespace Echo.Desktop.Features.Home;

public partial class RecentActivityEmptyView : UserControl
{
    public RecentActivityEmptyView() => InitializeComponent();
    public RecentActivityEmptyView(string title, string description, string icon = "history") : this()
    { EmptyTitle.Text = title; EmptyDescription.Text = description; EmptyIcon.Kind = icon; }
}
