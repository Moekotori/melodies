using System.Runtime.InteropServices;
using Echo.Contracts.Shortcuts;
using static Echo.Platform.Windows.WindowsShortcutNative;

namespace Echo.Platform.Windows;

// Lazy blocking message thread. At most 64 registrations and one pending replacement.
// Keyboard uses RegisterHotKey + MOD_NOREPEAT; mouse hook exists only for enabled mouse bindings.
public sealed class WindowsGlobalShortcuts : IGlobalShortcutBackend
{
    private const uint ApplyMessage = 0x8001;
    private readonly object _gate = new();
    private readonly Dictionary<int, (ShortcutBinding Binding, ShortcutGesture Gesture)> _registered = [];
    private readonly HashSet<string> _suppressedMouseUps = [];
    private readonly TaskCompletionSource<uint> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MouseHook _mouseCallback;
    private Request? _pending;
    private Thread? _thread;
    private nint _mouseHook;
    private bool _disposed;
    private sealed record Request(IReadOnlyList<ShortcutBinding> Bindings, TaskCompletionSource<IReadOnlyList<ShortcutRegistration>> Completion);
    public event Action<string>? Triggered;

    public WindowsGlobalShortcuts() => _mouseCallback = OnMouse;
    public async Task<IReadOnlyList<ShortcutRegistration>> ApplyAsync(IReadOnlyList<ShortcutBinding> bindings, CancellationToken cancellation)
    {
        if (bindings.Count > ShortcutLimits.MaxActions) throw new ArgumentOutOfRangeException(nameof(bindings));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread is null && bindings.Count == 0) return [];
            if (_thread is null) { _thread = new Thread(Run) { IsBackground = true, Name = "ECHO shortcuts" }; _thread.Start(); }
        }
        var id = await _ready.Task.WaitAsync(cancellation).ConfigureAwait(false);
        var completion = new TaskCompletionSource<IReadOnlyList<ShortcutRegistration>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pending is not null) throw new InvalidOperationException("Only one shortcut replacement may be pending.");
            _pending = new(bindings.ToArray(), completion);
            if (!PostThreadMessageW(id, ApplyMessage, 0, 0)) { _pending = null; throw new IOException("Shortcut message thread unavailable."); }
        }
        // Once submitted, finish replacement before accepting another. Cancellation is checked before submission.
        return await completion.Task.ConfigureAwait(false);
    }
    private void Run()
    {
        try
        {
            PeekMessageW(out _, 0, 0, 0, 0);
            _ready.TrySetResult(GetCurrentThreadId());
            while (GetMessageW(out var message, 0, 0, 0) > 0)
            {
                if (message.Id == ApplyMessage)
                {
                    Request? request;
                    lock (_gate) { request = _pending; _pending = null; }
                    if (request is not null)
                    {
                        try { request.Completion.TrySetResult(Replace(request.Bindings)); }
                        catch (Exception e) { request.Completion.TrySetException(e); }
                    }
                }
                else if (message.Id == 0x0312 && _registered.TryGetValue((int)message.WParam, out var binding))
                    Triggered?.Invoke(binding.Binding.Action);
            }
        }
        finally
        {
            Clear();
            lock (_gate) { _pending?.Completion.TrySetException(new ObjectDisposedException(nameof(WindowsGlobalShortcuts))); _pending = null; }
            _stopped.TrySetResult();
        }
    }
    private IReadOnlyList<ShortcutRegistration> Replace(IReadOnlyList<ShortcutBinding> bindings)
    {
        var desired = bindings.Where(x => x.Enabled && x.Scope == ShortcutScope.Global).ToArray();
        // Keep unchanged registrations, so editing a row cannot lose an unrelated OS reservation.
        foreach (var pair in _registered.ToArray())
            if (!desired.Contains(pair.Value.Binding)) { if (!pair.Value.Gesture.IsMouse) UnregisterHotKey(0, pair.Key); _registered.Remove(pair.Key); }
        var statuses = new List<ShortcutRegistration>(desired.Length);
        foreach (var binding in desired)
        {
            var gesture = ShortcutGesture.Parse(binding.Accelerator);
            if (gesture is null || !gesture.IsSafeGlobal)
            { statuses.Add(new(binding.Action, binding.Accelerator ?? "", false, "此组合不能用作全局快捷键。")); continue; }
            if (_registered.Values.Any(x => x.Binding == binding)) { statuses.Add(new(binding.Action, gesture.Accelerator, true)); continue; }
            var id = Enumerable.Range(1, ShortcutLimits.MaxActions).First(x => !_registered.ContainsKey(x));
            var success = gesture.IsMouse || VirtualKey(gesture.Key) is { } key && RegisterHotKey(0, id, (uint)gesture.Modifiers | 0x4000, key);
            if (success) _registered.Add(id, (binding, gesture));
            statuses.Add(new(binding.Action, gesture.Accelerator, success, success ? null : "被其他应用占用或系统保留，请换一个组合或重试。"));
        }
        var needsMouse = _registered.Values.Any(x => x.Gesture.IsMouse);
        if (needsMouse && _mouseHook == 0) _mouseHook = SetWindowsHookExW(14, _mouseCallback, 0, 0);
        if (!needsMouse && _mouseHook != 0) { UnhookWindowsHookEx(_mouseHook); _mouseHook = 0; _suppressedMouseUps.Clear(); }
        if (needsMouse && _mouseHook == 0)
            foreach (var pair in _registered.Where(x => x.Value.Gesture.IsMouse).ToArray())
            {
                _registered.Remove(pair.Key);
                var i = statuses.FindIndex(x => x.Action == pair.Value.Binding.Action);
                statuses[i] = statuses[i] with { Registered = false, Error = "无法注册鼠标侧键，请重试。" };
            }
        return statuses;
    }
    private nint OnMouse(int code, nint message, nint pointer)
    {
        if (code >= 0 && message is 0x0207 or 0x0208 or 0x020B or 0x020C)
        {
            var data = Marshal.PtrToStructure<MouseData>(pointer);
            if ((data.Flags & 1) == 0) // Ignore injected input; do not trigger from automation or replay.
            {
                var key = message is 0x0207 or 0x0208 ? "MouseButton3" : (data.Data >> 16) == 1 ? "MouseButton4" : "MouseButton5";
                if (message is 0x0208 or 0x020C)
                    return _suppressedMouseUps.Remove(key) ? 1 : CallNextHookEx(_mouseHook, code, message, pointer);
                var modifiers = (GetAsyncKeyState(0x11) < 0 ? ShortcutModifiers.Control : 0)
                    | (GetAsyncKeyState(0x12) < 0 ? ShortcutModifiers.Alt : 0) | (GetAsyncKeyState(0x10) < 0 ? ShortcutModifiers.Shift : 0)
                    | (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0 ? ShortcutModifiers.Meta : 0);
                foreach (var binding in _registered.Values)
                    if (binding.Gesture.Key == key && binding.Gesture.Modifiers == modifiers)
                    { _suppressedMouseUps.Add(key); Triggered?.Invoke(binding.Binding.Action); return 1; }
            }
        }
        return CallNextHookEx(_mouseHook, code, message, pointer);
    }
    private void Clear()
    {
        foreach (var pair in _registered) if (!pair.Value.Gesture.IsMouse) UnregisterHotKey(0, pair.Key);
        _registered.Clear(); _suppressedMouseUps.Clear(); if (_mouseHook != 0) { UnhookWindowsHookEx(_mouseHook); _mouseHook = 0; }
    }
    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; if (_thread is null) { Triggered = null; return; } }
        var id = await _ready.Task.ConfigureAwait(false);
        PostThreadMessageW(id, 0x0012, 0, 0);
        await _stopped.Task.ConfigureAwait(false); Triggered = null;
    }
    private static uint? VirtualKey(string key)
    {
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return key[0];
        if (key.StartsWith('F') && int.TryParse(key.AsSpan(1), out var f) && f is >= 1 and <= 24) return (uint)(0x70 + f - 1);
        if (key.Length == 4 && key.StartsWith("num", StringComparison.Ordinal) && char.IsAsciiDigit(key[3])) return (uint)(0x60 + key[3] - '0');
        return key switch
        {
            "Space" => 0x20, "Left" => 0x25, "Up" => 0x26, "Right" => 0x27, "Down" => 0x28,
            "Enter" => 0x0D, "Esc" => 0x1B, "Tab" => 0x09, "Delete" => 0x2E, "Backspace" => 0x08,
            "Home" => 0x24, "End" => 0x23, "PageUp" => 0x21, "PageDown" => 0x22, "Insert" => 0x2D,
            "CapsLock" => 0x14, "NumLock" => 0x90, "ScrollLock" => 0x91, "Pause" => 0x13, "PrintScreen" => 0x2C, "ContextMenu" => 0x5D,
            "BrowserBack" => 0xA6, "BrowserForward" => 0xA7, "BrowserRefresh" => 0xA8, "BrowserStop" => 0xA9,
            "BrowserSearch" => 0xAA, "BrowserFavorites" => 0xAB, "BrowserHome" => 0xAC,
            "MediaPlayPause" => 0xB3, "MediaNextTrack" => 0xB0, "MediaPreviousTrack" => 0xB1, "MediaStop" => 0xB2,
            "VolumeUp" => 0xAF, "VolumeDown" => 0xAE, "VolumeMute" => 0xAD,
            "=" => 0xBB, "-" => 0xBD, "," => 0xBC, "." => 0xBE, "/" => 0xBF, ";" => 0xBA,
            "[" => 0xDB, "]" => 0xDD, "\\" => 0xDC, "'" => 0xDE, "`" => 0xC0,
            "numadd" => 0x6B, "numsub" => 0x6D, "nummult" => 0x6A, "numdiv" => 0x6F, "numdec" => 0x6E, _ => null
        };
    }
}
