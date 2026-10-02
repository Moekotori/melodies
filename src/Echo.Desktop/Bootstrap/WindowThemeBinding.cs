using Echo.Desktop.Motion;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

using Echo.Desktop.Theming;

namespace Echo.Desktop.Bootstrap;

// All theme resources, platform subscription and one minute schedule belong to this window.
internal sealed class WindowThemeBinding : IDisposable
{
    private readonly Window _window;
    private readonly ThemeViewModel _theme;
    private readonly MotionPolicy _motion;
    private readonly Action<bool> _setDark;
    private readonly IPlatformSettings? _platform;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly ThemeWallpaper _wallpaper;
    public bool IsApplying { get; private set; }
    private bool _disposed;
    public WindowThemeBinding(Window window, ThemeViewModel theme, MotionPolicy motion, Action<bool> setDark)
    {
        _window = window; _theme = theme; _motion = motion; _setDark = setDark; _platform = global::Avalonia.Application.Current?.PlatformSettings;
        _wallpaper = new(window, theme);
        theme.Changed += Apply;
        if (_platform is not null) _platform.ColorValuesChanged += OnPlatformChanged;
        _timer.Tick += Tick; window.Activated += Activated; window.PropertyChanged += WindowChanged; Apply();
    }
    private void Tick(object? sender, EventArgs args) => Apply();
    private void Activated(object? sender, EventArgs args) => Apply();
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    { if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty) UpdateTimer(); }
    private void UpdateTimer()
    {
        if (_theme.ScheduleEnabled && _window.IsVisible && _window.WindowState != WindowState.Minimized) _timer.Start();
        else _timer.Stop();
    }
    private void OnPlatformChanged(object? sender, PlatformColorValues args) => Dispatcher.UIThread.Post(() => { if (!_disposed) Apply(); });
    private void Apply()
    {
        if (_disposed) return;
        IsApplying = true;
        try
        {
            var preferences = _theme.Snapshot; var system = _platform?.GetColorValues();
            var dark = preferences.Mode == "dark" || (preferences.Mode is "system" or "ambient" or "acrylic" && system?.ThemeVariant == PlatformThemeVariant.Dark);
            if (preferences.ScheduleEnabled && TimeOnly.TryParseExact(preferences.DarkAt, "HH:mm", out var atDark) && TimeOnly.TryParseExact(preferences.LightAt, "HH:mm", out var atLight))
                dark = ThemeViewModel.ScheduledDark(TimeOnly.FromDateTime(DateTime.Now), atDark, atLight);
            if (preferences.Invert) dark = !dark;
            // Set both tone dictionaries first. Popups and theme-scoped controls inherit the same resources.
            foreach (var toneDark in new[] { false, true })
                _window.Resources.ThemeDictionaries[toneDark ? ThemeVariant.Dark : ThemeVariant.Light] = ThemeResourcePalette.Create(_theme.Tone(toneDark), preferences, toneDark, system?.AccentColor1);
            _setDark(dark); _window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            _window.TransparencyLevelHint = preferences.Mode == "acrylic" ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent] : [WindowTransparencyLevel.None];
            var tone = _theme.Tone(dark);
            _motion.ThemeAllowsAnimation = tone.MotionEnabled;
            _motion.ThemeDurationScale = tone.Numbers["motionSpeedSeconds"] / .22;
            _motion.ThemeIntensityScale = tone.Numbers["motionIntensityPercent"] / 100;
            _wallpaper.Apply(preferences, tone); UpdateTimer();
        }
        finally { IsApplying = false; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _timer.Stop(); _timer.Tick -= Tick;
        _window.Activated -= Activated; _window.PropertyChanged -= WindowChanged;
        _theme.Changed -= Apply;
        if (_platform is not null) _platform.ColorValuesChanged -= OnPlatformChanged;
        _wallpaper.Dispose();
    }
}

