using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.LibraryDetails;

// A detail page owns its inner padding; its scroll viewer fills the shell surface.
// Reference counting also covers the two views alive during a page transition.
internal sealed class DetailPageInsetLease : IDisposable
{
    private static readonly ConditionalWeakTable<Control, InsetState> States = new();
    private Control? _page;
    private DetailPageInsetLease(Control page) => _page = page;

    public static DetailPageInsetLease? Acquire(Control detail)
    {
        var host = detail.GetVisualAncestors().OfType<MotionPresenter>().FirstOrDefault(view => view.Name == "PageHost");
        if (host?.Content is not { } page) return null;
        var state = States.GetValue(page, control => new(control.Margin));
        state.Count++;
        page.Margin = default;
        return new(page);
    }

    public void Dispose()
    {
        if (_page is not { } page) return;
        _page = null;
        if (!States.TryGetValue(page, out var state) || --state.Count != 0) return;
        page.Margin = state.Margin;
        States.Remove(page);
    }

    private sealed class InsetState(Thickness margin)
    {
        public Thickness Margin { get; } = margin;
        public int Count;
    }
}
