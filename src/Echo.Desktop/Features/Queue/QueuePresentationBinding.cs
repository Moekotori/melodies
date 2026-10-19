using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Queue;

// Visibility belongs to the containing surface, not to a list hidden by its own empty state.
// Multiple drawers can briefly coexist during navigation; each releases only its own demand.
internal sealed class QueuePresentationBinding
{
    private readonly Control surface;
    private readonly Func<QueueViewModel?> resolve;
    private readonly List<Visual> ancestors = [];
    private QueueViewModel? owner;
    private IDisposable? lease;
    public QueuePresentationBinding(Control surface, Func<QueueViewModel?> resolve)
    {
        this.surface = surface; this.resolve = resolve;
        surface.AttachedToVisualTree += (_, _) => Attach();
        surface.DetachedFromVisualTree += (_, _) => Detach();
        surface.DataContextChanged += (_, _) => Update();
        if (surface.IsAttachedToVisualTree()) Attach();
    }
    private void Attach()
    {
        Detach();
        ancestors.Add(surface); ancestors.AddRange(surface.GetVisualAncestors());
        foreach (var visual in ancestors) visual.PropertyChanged += VisibilityChanged;
        Update();
    }
    private void Detach()
    {
        foreach (var visual in ancestors) visual.PropertyChanged -= VisibilityChanged;
        ancestors.Clear(); Release();
    }
    private void VisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty) Update(); }
    private void Update()
    {
        var visible = ancestors.Count > 0 && ancestors.All(visual => visual.IsVisible
            && (visual is not Window window || window.WindowState != WindowState.Minimized));
        var next = visible ? resolve() : null;
        if (ReferenceEquals(next, owner)) return;
        Release(); owner = next; lease = next?.AcquirePresentation();
    }
    private void Release() { lease?.Dispose(); lease = null; owner = null; }
}
