using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Echo.Contracts.Shortcuts;

namespace Echo.Desktop.Features.Shortcuts;

internal static class ShortcutInput
{
    internal static bool IsEditing(object? source) => source is Control control
        && control.GetVisualAncestors().Prepend(control).Any(x => x is TextBox or NumericUpDown or ComboBox or Slider or Echo.Desktop.Theming.ThemeChoiceList or Echo.Desktop.Theming.ThemeSpectrum
            || x is ListBox list && list.Classes.Contains("theme-color-list"));
    internal static ShortcutModifiers Modifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? ShortcutModifiers.Control : 0)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? ShortcutModifiers.Alt : 0)
        | (modifiers.HasFlag(KeyModifiers.Shift) ? ShortcutModifiers.Shift : 0)
        | (modifiers.HasFlag(KeyModifiers.Meta) ? ShortcutModifiers.Meta : 0);
    internal static string? FromKey(KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin => null,
            Key.OemPlus => "=", Key.OemMinus => "-", Key.OemComma => ",", Key.OemPeriod => ".", Key.OemQuestion => "/",
            Key.OemSemicolon => ";", Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]", Key.OemPipe => "\\",
            Key.OemQuotes => "'", Key.OemTilde => "`", Key.Add => "numadd", Key.Subtract => "numsub", Key.Multiply => "nummult",
            Key.Divide => "numdiv", Key.Decimal => "numdec", _ => e.Key.ToString()
        };
        if (key is null) return null;
        if (e.Key is >= Key.D0 and <= Key.D9) key = ((int)e.Key - (int)Key.D0).ToString();
        if (e.Key is >= Key.NumPad0 and <= Key.NumPad9) key = "num" + ((int)e.Key - (int)Key.NumPad0);
        var parsed = ShortcutGesture.Parse(key);
        return parsed is null ? null : new ShortcutGesture(Modifiers(e.KeyModifiers), parsed.Key).Accelerator;
    }
    internal static string? FromMouse(PointerPressedEventArgs e, Control relativeTo)
    {
        var kind = e.GetCurrentPoint(relativeTo).Properties.PointerUpdateKind;
        var key = kind switch { PointerUpdateKind.MiddleButtonPressed => "MouseButton3", PointerUpdateKind.XButton1Pressed => "MouseButton4", PointerUpdateKind.XButton2Pressed => "MouseButton5", _ => null };
        return key is null ? null : new ShortcutGesture(Modifiers(e.KeyModifiers), key).Accelerator;
    }
}
