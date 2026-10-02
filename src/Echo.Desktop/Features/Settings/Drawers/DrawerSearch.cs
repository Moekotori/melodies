using Avalonia.Controls;
using Avalonia.Threading;

namespace Echo.Desktop.Features.Settings.Drawers;

internal sealed record DrawerSearchEntry(int Tab, string Terms, string Control);
internal sealed class DrawerSearch(UserControl view, TabControl tabs, IReadOnlyList<DrawerSearchEntry> entries)
{
    private Control? highlighted;
    private int generation;
    public bool Find(string query)
    {
        var version = ++generation;
        highlighted?.Classes.Remove("drawer-match"); highlighted = null;
        var words = query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return true;
        var match = entries.FirstOrDefault(entry => words.All(word => entry.Terms.Contains(word, StringComparison.OrdinalIgnoreCase)));
        if (match is null) return false;
        tabs.SelectedIndex = match.Tab;
        Dispatcher.UIThread.Post(() =>
        {
            if (version != generation) return;
            highlighted = view.FindControl<Control>(match.Control);
            highlighted?.Classes.Add("drawer-match"); highlighted?.BringIntoView();
        }, DispatcherPriority.Loaded);
        return true;
    }
    public void Cancel() { generation++; highlighted?.Classes.Remove("drawer-match"); highlighted = null; }
}
