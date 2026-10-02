namespace Echo.Contracts.Shortcuts;

[Flags]
public enum ShortcutModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Meta = 8 }

public sealed record ShortcutGesture(ShortcutModifiers Modifiers, string Key)
{
    public string Accelerator => string.Concat(Modifiers.HasFlag(ShortcutModifiers.Control) ? "Ctrl+" : "",
        Modifiers.HasFlag(ShortcutModifiers.Meta) ? "Super+" : "", Modifiers.HasFlag(ShortcutModifiers.Alt) ? "Alt+" : "",
        Modifiers.HasFlag(ShortcutModifiers.Shift) ? "Shift+" : "", Key);
    public bool IsMouse => Key is "MouseButton3" or "MouseButton4" or "MouseButton5";
    public bool IsSafeGlobal => !(Key == "F4" && Modifiers.HasFlag(ShortcutModifiers.Alt))
        && !(Key == "Delete" && Modifiers.HasFlag(ShortcutModifiers.Control) && Modifiers.HasFlag(ShortcutModifiers.Alt))
        && !(Modifiers.HasFlag(ShortcutModifiers.Meta) && Key is "L" or "D" or "Tab");

    public static ShortcutGesture? Parse(string? input, bool commandIsMeta = false)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 96) return null;
        var parts = input.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length > 5 || parts.Any(string.IsNullOrEmpty)) return null;
        var modifiers = ShortcutModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var modifier = parts[i].ToLowerInvariant() switch
            {
                "control" or "ctrl" => ShortcutModifiers.Control, "alt" or "option" => ShortcutModifiers.Alt,
                "shift" => ShortcutModifiers.Shift, "super" or "meta" or "cmd" or "command" => ShortcutModifiers.Meta,
                "cmdorctrl" or "commandorcontrol" => commandIsMeta ? ShortcutModifiers.Meta : ShortcutModifiers.Control,
                _ => ShortcutModifiers.None
            };
            if (modifier == ShortcutModifiers.None || modifiers.HasFlag(modifier)) return null;
            modifiers |= modifier;
        }
        var token = parts[^1];
        var key = token.ToLowerInvariant() switch
        {
            "space" or "spacebar" => "Space", "left" or "arrowleft" => "Left", "right" or "arrowright" => "Right",
            "up" or "arrowup" => "Up", "down" or "arrowdown" => "Down", "escape" or "esc" => "Esc",
            "return" or "enter" => "Enter", "delete" => "Delete", "backspace" => "Backspace", "tab" => "Tab",
            "home" => "Home", "end" => "End", "pageup" => "PageUp", "pagedown" => "PageDown", "insert" => "Insert",
            "mediaplaypause" => "MediaPlayPause", "medianexttrack" => "MediaNextTrack", "mediaprevioustrack" => "MediaPreviousTrack",
            "mediastop" => "MediaStop", "volumeup" => "VolumeUp", "volumedown" => "VolumeDown", "volumemute" => "VolumeMute",
            "mousebutton3" => "MouseButton3", "mousebutton4" or "xbutton1" => "MouseButton4", "mousebutton5" or "xbutton2" => "MouseButton5",
            "plus" or "numadd" => "numadd", "numsub" => "numsub", "nummult" => "nummult", "numdiv" => "numdiv", "numdec" => "numdec",
            _ => token.ToUpperInvariant()
        };
        if (key.Length == 1 && (char.IsAsciiLetterOrDigit(key[0]) || "-=[],.;'/\\`".Contains(key[0]))) return new(modifiers, key);
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24) return new(modifiers, key);
        if (token.Length == 4 && token.StartsWith("num", StringComparison.OrdinalIgnoreCase) && char.IsAsciiDigit(token[3])) return new(modifiers, token.ToLowerInvariant());
        return key is "Space" or "Left" or "Right" or "Up" or "Down" or "Esc" or "Enter" or "Delete" or "Backspace" or "Tab"
            or "Home" or "End" or "PageUp" or "PageDown" or "Insert" or "MediaPlayPause" or "MediaNextTrack" or "MediaPreviousTrack"
            or "MediaStop" or "VolumeUp" or "VolumeDown" or "VolumeMute" or "MouseButton3" or "MouseButton4" or "MouseButton5"
            or "numadd" or "numsub" or "nummult" or "numdiv" or "numdec" ? new(modifiers, key) : null;
    }
}
