using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Echo.Desktop.Bootstrap;

// Input mode belongs to a window, not a page. Weak keys do not retain closed windows.
internal static class FocusPolicy
{
    private sealed class InputState { internal bool Keyboard; internal long Revision; }
    private static readonly ConditionalWeakTable<TopLevel, InputState> States = new();
    private static bool installed;

    internal static void Install()
    {
        if (installed) return;
        installed = true;
        Window.IsActiveProperty.Changed.AddClassHandler<Window>((window, _) =>
        {
            if (window.IsActive) return;
            var state = States.GetOrCreateValue(window); state.Keyboard = false; state.Revision++;
        });
        InputElement.TextInputEvent.AddClassHandler<TopLevel>((top, _) =>
        {
            var state = States.GetOrCreateValue(top); state.Keyboard = true; state.Revision++;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.KeyDownEvent.AddClassHandler<TopLevel>((top, args) =>
        {
            var state = States.GetOrCreateValue(top); state.Keyboard = true; state.Revision++;
            if (!args.Handled && args.Key == Key.Tab && args.KeyModifiers is KeyModifiers.None or KeyModifiers.Shift
                && top.FocusManager?.GetFocusedElement() is Control scope
                && KeyboardNavigation.GetTabNavigation(scope) == KeyboardNavigationMode.Cycle)
            {
                var targets = scope.GetVisualDescendants().OfType<Control>().Where(control =>
                    control.Focusable && control.IsTabStop && control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
                var target = args.KeyModifiers == KeyModifiers.Shift ? targets.LastOrDefault() : targets.FirstOrDefault();
                if (target?.Focus(NavigationMethod.Tab, args.KeyModifiers) == true) args.Handled = true;
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.PointerPressedEvent.AddClassHandler<TopLevel>((top, _) =>
        {
            var state = States.GetOrCreateValue(top); state.Keyboard = false; state.Revision++;
            if (top.FocusManager?.GetFocusedElement() is Button button && !IsEditorPart(button))
                ReleaseButton(button, top);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        InputElement.GettingFocusEvent.AddClassHandler<Button>((button, args) =>
        {
            if (!ReferenceEquals(args.Source, button) || IsEditorPart(button)
                || TopLevel.GetTopLevel(button) is not { } top) return;
            if (args.NavigationMethod == NavigationMethod.Pointer || !States.GetOrCreateValue(top).Keyboard)
            {
                if (ModalScope(button) is { } scope) args.TrySetNewFocusedElement(scope);
                else args.TrySetNewFocusedElement(null!); // Avalonia accepts null to clear focus.
            }
        });
    }

    internal static long InputRevision(Control owner) => TopLevel.GetTopLevel(owner) is { } top
        ? States.GetOrCreateValue(top).Revision : -1;

    internal static bool IsCurrent(Control owner, long revision) => revision >= 0
        && owner.IsEffectivelyVisible && owner.IsAttachedToVisualTree() && InputRevision(owner) == revision;

    internal static void Restore(Control closingSurface, IInputElement? previous)
    {
        if (TopLevel.GetTopLevel(closingSurface) is not { } top) return;
        var focused = top.FocusManager?.GetFocusedElement();
        if (focused is Avalonia.Visual current && !ReferenceEquals(current, closingSurface)
            && !closingSurface.IsVisualAncestorOf(current)) return;
        if (previous is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } target
            && target.IsAttachedToVisualTree() && TopLevel.GetTopLevel(target) == top
            && !closingSurface.IsVisualAncestorOf(target)) target.Focus(NavigationMethod.Tab);
        else top.FocusManager?.Focus(null);
    }

    private static bool IsEditorPart(Control control)
    {
        for (var visual = control.GetVisualParent(); visual is not null; visual = visual.GetVisualParent())
            if (visual is TextBox or NumericUpDown or ComboBox or Slider) return true;
        return false;
    }

    private static void ReleaseButton(Button button, TopLevel top)
    {
        if (ModalScope(button) is { } scope) scope.Focus(NavigationMethod.Pointer);
        else top.FocusManager?.Focus(null);
    }

    private static Control? ModalScope(Button button)
    {
        // Keep Tab inside a modal without arming its close/action button with Space.
        for (var visual = button.GetVisualParent(); visual is not null; visual = visual.GetVisualParent())
            if (visual is Control scope && KeyboardNavigation.GetTabNavigation(scope) == KeyboardNavigationMode.Cycle)
            {
                scope.Focusable = true; scope.IsTabStop = false;
                return scope;
            }
        return null;
    }
}
