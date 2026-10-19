using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace Echo.Desktop.Bootstrap;

// Avalonia 12.1.3 keeps remembered focus and dirty visuals while a window is hidden.
// Isolate this version-specific adaptation; it never renders or restarts a hidden window.
internal static class AvaloniaSurfaceRelease
{
    private const string RendererType = "Avalonia.Rendering.Composition.CompositingRenderer, Avalonia.Base";

    internal static void ReleaseFocus(Window window, Control surface)
    {
        foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegisteredAttached(typeof(Window)))
            if (property.OwnerType == typeof(Avalonia.Input.FocusManager) && property.Name == "FocusedElement"
                && window.GetValue(property) is Visual remembered && surface.IsVisualAncestorOf(remembered))
                window.ClearValue(property);
        if (window.FocusManager?.GetFocusedElement() is Visual focused && surface.IsVisualAncestorOf(focused))
            window.FocusManager.Focus(null);
    }

    internal static void ReleaseDetachedVisuals(Window window)
    {
        var renderer = GetRenderer(window);
        // Detached visuals have no composition visual to update. Keep all live work,
        // including the empty window's pending scene update for its next restore.
        Dirty(renderer).RemoveWhere(static visual => !visual.IsAttachedToVisualTree());
        RecalculateChildren(renderer).RemoveWhere(static visual => !visual.IsAttachedToVisualTree());
        foreach (var visual in Dirty(renderer)) SynchronizeChildren(visual);
        foreach (var visual in RecalculateChildren(renderer)) SynchronizeChildren(visual);
        _ = ElementComposition.GetElementVisual(window)?.Compositor.RequestCompositionBatchCommitAsync();
    }

    // .NET 10 accessors are resolved by the runtime/AOT compiler, without reflection.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Renderer")]
    [return: UnsafeAccessorType(RendererType)]
    private static extern object GetRenderer(TopLevel window);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_dirty")]
    private static extern ref HashSet<Visual> Dirty([UnsafeAccessorType(RendererType)] object renderer);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_recalculateChildren")]
    private static extern ref HashSet<Visual> RecalculateChildren([UnsafeAccessorType(RendererType)] object renderer);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "SynchronizeCompositionChildVisuals")]
    private static extern void SynchronizeChildren(Visual visual);
}
