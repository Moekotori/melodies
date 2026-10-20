using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Echo.Desktop.Bootstrap;

AppBuilder.Configure<FocusApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
var first = new Button { Content = "Open drawer", Width = 180, Height = 44 };
var second = new Button { Content = "Next action", Width = 180, Height = 44 };
var input = new TextBox { Width = 180, Height = 44 };
var otherInput = new TextBox { Width = 180, Height = 44 };
var modal = new StackPanel();
var close = new Button { Name = "ModalClose", Content = "Close", Width = 180, Height = 44 };
modal.Children.Add(close); KeyboardNavigation.SetTabNavigation(modal, KeyboardNavigationMode.Cycle);
var root = new StackPanel { Children = { first, second, input, modal, otherInput } };
var window = new Window { Width = 400, Height = 300, Content = root };
var clicks = 0; first.Click += (_, _) => clicks++;
window.Show(); Settle();
try
{
    Click(first);
    Check(clicks == 1 && !first.IsFocused, "mouse click executes without retaining action focus");
    Press(Key.Space); Check(clicks == 1, "Space does not repeat a mouse-clicked action");
    Click(first); Check(clicks == 2 && !first.IsFocused, "repeated mouse click still executes");
    first.Focus(); Check(!first.IsFocused, "programmatic button focus after pointer input is suppressed");
    Press(Key.Tab); Check(first.IsFocused, "Tab focuses first button");
    Press(Key.Space); Check(clicks == 3, "Space activates a deliberately keyboard-focused button");
    input.Focus(); Check(input.IsFocused, "explicit text input focus is retained");
    var revision = FocusPolicy.InputRevision(input);
    Check(FocusPolicy.IsCurrent(input, revision), "current deferred request");
    Press(Key.Right); Check(!FocusPolicy.IsCurrent(input, revision), "later keyboard input invalidates deferred focus");
    Click(first); close.Focus();
    Check(!close.IsFocused && modal.IsFocused, "pointer-opened modal keeps neutral focus inside boundary");
    Press(Key.Tab); Check(close.IsFocused, $"Tab enters modal controls: {window.FocusManager?.GetFocusedElement()}, modal={modal.IsFocused}, close={close.IsFocused}");
    Click(first); input.Focus(); window.KeyTextInput("hello world");
    Check(input.Text == "hello world", "text entry and spaces remain intact");
    var typingRevision = FocusPolicy.InputRevision(input);
    window.KeyTextInput("!");
    Check(!FocusPolicy.IsCurrent(input, typingRevision), "text composition invalidates deferred focus");
    otherInput.Focus(); FocusPolicy.Restore(modal, input);
    Check(otherInput.IsFocused, "closing a different surface must not steal the new focus");
    close.Focus(NavigationMethod.Pointer); FocusPolicy.Restore(modal, input);
    Check(input.IsFocused, "closing a surface restores its still-valid input owner");
    input.IsVisible = false;
    Check(!FocusPolicy.IsCurrent(input, FocusPolicy.InputRevision(input)), "hidden target cannot claim delayed focus");
    Console.WriteLine("PASS focus policy: mouse actions, Space, repeated clicks, Tab navigation, modal boundary, text entry and stale requests.");
}
finally { window.Close(); }

void Settle() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
void Click(Control control)
{
    var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
    window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Settle();
}
void Press(Key key)
{
    window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
    window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null); Settle();
}
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
sealed class FocusApp : Application
{
    public override void Initialize() { FocusPolicy.Install(); Styles.Add(new Semi.Avalonia.SemiTheme()); }
}
