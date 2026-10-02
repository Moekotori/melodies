namespace Echo.Platform.Windows;

public sealed record TaskbarPlayerState(bool Enabled, bool Playing, bool CanPrevious, bool CanPlay, bool CanNext,
    bool CanFavorite, bool Favorite, bool ProgressEnabled, uint ProgressState, double Position, double Duration,
    string Title, string[] Tooltips);

// UI-thread owned. No timers, file reads, playback owner, or retained GDI bitmap.
public sealed class WindowsTaskbarPlayer : IDisposable
{
    private nint _taskbar;
    private readonly nint _window;
    private readonly List<nint> _icons = [];
    private TaskbarPlayerState? _state;
    private TaskbarPlayerState? _applied;
    private bool _buttonsAdded, _disposed, _comInitialized;
    private byte[]? _artwork;
    private const int ArtworkSize = 256;
    public uint CreatedMessage { get; } = TaskbarNative.RegisterWindowMessageW("TaskbarButtonCreated");
    public event Action<int>? Command;
    public event Action? ArtworkRequested;
    public event Action<string>? Failed;
    public bool IsReady => _taskbar != 0 && _buttonsAdded;

    public WindowsTaskbarPlayer(nint window) => _window = window;
    public void Initialize()
    {
        if (_disposed) return;
        Try(() =>
        {
            TaskbarNative.Release(_taskbar, _comInitialized); _taskbar = 0; _comInitialized = false;
            _taskbar = TaskbarNative.Create(out _comInitialized); _buttonsAdded = false; _applied = null;
            if (_state is not null) Apply(_state);
        });
    }
    public void SetIcons(IReadOnlyList<byte[]> pixels)
    {
        if (pixels.Count != 7) throw new ArgumentException("Seven taskbar glyphs are required.");
        var next = new List<nint>();
        try { foreach (var image in pixels) next.Add(TaskbarBitmap.Icon(image, 32)); }
        catch { foreach (var icon in next) TaskbarBitmap.DestroyIcon(icon); throw; }
        var old = _icons.ToArray(); _icons.Clear(); _icons.AddRange(next);
        _applied = null;
        if (_state is not null) Publish(_state);
        foreach (var icon in old) TaskbarBitmap.DestroyIcon(icon);
    }
    public void Publish(TaskbarPlayerState state)
    {
        if (_disposed) return;
        var changedPreview = _state?.Enabled != state.Enabled;
        _state = state;
        Try(() =>
        {
            var value = state.Enabled ? 1 : 0;
            if (changedPreview)
            {
                TaskbarNative.Check(TaskbarNative.DwmSetWindowAttribute(_window, 7, ref value, 4));
                TaskbarNative.Check(TaskbarNative.DwmSetWindowAttribute(_window, 10, ref value, 4));
                TaskbarNative.DwmInvalidateIconicBitmaps(_window);
            }
            if (_taskbar != 0) Apply(state);
        });
    }
    private void Apply(TaskbarPlayerState state)
    {
        if (_icons.Count == 7 && (_applied is null || state.Enabled != _applied.Enabled || state.Playing != _applied.Playing
            || state.CanPrevious != _applied.CanPrevious || state.CanPlay != _applied.CanPlay || state.CanNext != _applied.CanNext
            || state.CanFavorite != _applied.CanFavorite || state.Favorite != _applied.Favorite || !state.Tooltips.SequenceEqual(_applied.Tooltips)))
        {
            var enabled = new[] { state.CanPrevious, state.CanPlay, state.CanNext, state.CanFavorite, true };
            var icons = new[] { 0, state.Playing ? 2 : 1, 3, state.Favorite ? 5 : 4, 6 };
            var buttons = Enumerable.Range(0, 5).Select(i => TaskbarNative.ThumbButton.Create((uint)i + 1,
                _icons[icons[i]], state.Tooltips[i], enabled[i], state.Enabled)).ToArray();
            try { TaskbarNative.Buttons(_taskbar, _window, buttons, !_buttonsAdded); }
            catch (System.Runtime.InteropServices.COMException) when (!_buttonsAdded)
            { TaskbarNative.Buttons(_taskbar, _window, buttons, false); }
            _buttonsAdded = true;
        }
        if (_applied?.Title != state.Title) TaskbarNative.Tooltip(_taskbar, _window, state.Title);
        var total = double.IsFinite(state.Duration) ? Math.Max(1, state.Duration * 1000) : 1;
        var position = double.IsFinite(state.Position) ? Math.Clamp(state.Position * 1000, 0, total) : 0;
        TaskbarNative.Progress(_taskbar, _window, state.ProgressEnabled ? state.ProgressState : 0,
            (ulong)position, (ulong)total);
        _applied = state;
    }
    public void SetArtwork(byte[] pixels)
    {
        if (pixels.Length != ArtworkSize * ArtworkSize * 4) throw new ArgumentException("Expected 256px BGRA artwork.");
        _artwork = pixels;
        if (!_disposed && _state?.Enabled == true) TaskbarNative.DwmInvalidateIconicBitmaps(_window);
    }
    public void ClearArtwork() => _artwork = null;
    public bool HandleMessage(uint message, nint wParam, nint lParam)
    {
        if (_disposed) return false;
        if (message == CreatedMessage) { Initialize(); return false; }
        if (message == 0x111 && ((long)wParam >> 16 & 0xffff) == 0x1800 && _state?.Enabled == true)
        {
            var id = (int)((long)wParam & 0xffff);
            var allowed = id switch { 1 => _state.CanPrevious, 2 => _state.CanPlay, 3 => _state.CanNext,
                4 => _state.CanFavorite, 5 => true, _ => false };
            if (allowed) Command?.Invoke(id);
            return id is >= 1 and <= 5;
        }
        if (message is not (0x323 or 0x326) || _state?.Enabled != true) return false;
        ArtworkRequested?.Invoke();
        if (_artwork is null) return false;
        Try(() =>
        {
            var size = message == 0x323
                ? Math.Clamp(Math.Min((int)((long)lParam >> 16 & 0xffff), (int)((long)lParam & 0xffff)), 1, 512)
                : ArtworkSize;
            var bitmap = TaskbarBitmap.Create(TaskbarBitmap.Scale(_artwork, ArtworkSize, size), size, size);
            try
            {
                TaskbarNative.Check(message == 0x323 ? TaskbarNative.DwmSetIconicThumbnail(_window, bitmap, 0)
                    : TaskbarNative.DwmSetIconicLivePreviewBitmap(_window, bitmap, 0, 0));
            }
            finally { TaskbarBitmap.DeleteObject(bitmap); }
        });
        return true;
    }
    private void Try(Action action)
    { try { action(); } catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        { Failed?.Invoke($"Windows taskbar: {error.Message}"); } }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Try(() =>
        {
            if (_taskbar != 0)
            {
                if (_state is not null) Apply(_state with { Enabled = false, ProgressEnabled = false });
                TaskbarNative.Progress(_taskbar, _window, 0, 0, 1);
            }
            var disabled = 0;
            TaskbarNative.DwmSetWindowAttribute(_window, 7, ref disabled, 4);
            TaskbarNative.DwmSetWindowAttribute(_window, 10, ref disabled, 4);
        });
        TaskbarNative.Release(_taskbar, _comInitialized); _taskbar = 0; _comInitialized = false;
        foreach (var icon in _icons) TaskbarBitmap.DestroyIcon(icon);
        _icons.Clear(); _artwork = null; Command = null; ArtworkRequested = null; Failed = null;
    }
}
