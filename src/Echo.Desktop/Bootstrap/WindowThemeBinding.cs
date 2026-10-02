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
    private readonly ThemeRainbowBinding _rainbow;
    private readonly ThemeFontBinding _fonts;
    public bool IsApplying { get; private set; }
    private bool _disposed;
    public WindowThemeBinding(Window window, ThemeViewModel theme, MotionPolicy motion, Action<bool> setDark)
    {
        _window = window; _theme = theme; _motion = motion; _setDark = setDark; _platform = global::Avalonia.Application.Current?.PlatformSettings;
        _wallpaper = new(window, theme); _rainbow = new(window, theme, motion); _fonts = new(theme, Apply);
        _wallpaper.AvailabilityChanged += Apply;
        _theme.WallpaperReloadRequested += ReloadWallpaper;
        window.SizeChanged += Sized; _motion.PropertyChanged += MotionChanged;
        theme.Changed += Apply;
        if (_platform is not null) _platform.ColorValuesChanged += OnPlatformChanged;
        _timer.Tick += Tick; window.Activated += Activated; window.Deactivated += Activated; window.PropertyChanged += WindowChanged; Apply();
    }
    private void Tick(object? sender, EventArgs args) => Apply();
    private void ReloadWallpaper() { _wallpaper.InvalidateSource(); Apply(); }
    private void Sized(object? sender, SizeChangedEventArgs args) => Apply();
    private void MotionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    { if (!IsApplying && args.PropertyName is nameof(MotionPolicy.CanAnimate) or nameof(MotionPolicy.UseSpatialMotion)) Apply(); }
    private void Activated(object? sender, EventArgs args) => Apply();
    private void WindowChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == Visual.IsVisibleProperty || args.Property == Window.WindowStateProperty) Apply();
        if (args.Property == TopLevel.ActualTransparencyLevelProperty && !IsApplying) Apply();
    }
    private void UpdateTimer()
    {
        if (_theme.ScheduleEnabled && _window.IsVisible && _window.WindowState != WindowState.Minimized)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(500, 60000 - DateTime.Now.TimeOfDay.TotalMilliseconds % 60000));
            _timer.Start();
        }
        else _timer.Stop();
    }
    private void OnPlatformChanged(object? sender, PlatformColorValues args) => Dispatcher.UIThread.Post(() => { if (!_disposed) Apply(); });
    private void Apply()
    {
        if (_disposed || IsApplying) return;
        IsApplying = true;
        try
        {
            var preferences = _theme.Snapshot; var system = _platform?.GetColorValues();
            var dark = preferences.Mode is "dark" or "ambient" || (preferences.Mode is "system" or "acrylic" && system?.ThemeVariant == PlatformThemeVariant.Dark);
            if (preferences.Mode != "ambient" && preferences.ScheduleEnabled && TimeOnly.TryParseExact(preferences.DarkAt, "HH:mm", out var atDark) && TimeOnly.TryParseExact(preferences.LightAt, "HH:mm", out var atLight))
                dark = ThemeViewModel.ScheduledDark(TimeOnly.FromDateTime(DateTime.Now), atDark, atLight);
            var requestedAcrylic = preferences.AcrylicEnabled || preferences.Mode == "acrylic";
            var useAcrylic = requestedAcrylic && (_window.IsActive || preferences.AcrylicKeepUnfocused);
            _window.TransparencyLevelHint = useAcrylic ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.None] : [WindowTransparencyLevel.None];
            var availableAcrylic = useAcrylic && _window.ActualTransparencyLevel != WindowTransparencyLevel.None;
            _theme.AcrylicStatus = !requestedAcrylic ? "" : !useAcrylic ? "窗口失焦时使用实色" : availableAcrylic ? "系统模糊已启用" : "当前平台未提供系统模糊，已使用实色背景";
            var tone = _theme.Tone(dark);
            _motion.ThemeAllowsAnimation = tone.MotionEnabled;
            _motion.ThemeDurationScale = tone.Numbers["motionSpeedSeconds"] / .22;
            _motion.ThemeIntensityScale = tone.Numbers["motionIntensityPercent"] / 100;
            _fonts.Apply(preferences.Appearance ?? new()); _wallpaper.Apply(preferences, tone, _motion.UseSpatialMotion);
            // Set both tone dictionaries first. Popups and theme-scoped controls inherit the same resources.
            foreach (var toneDark in new[] { false, true })
            {
                var resources = ThemeResourcePalette.Create(_theme.Tone(toneDark), preferences, toneDark, system?.AccentColor1, availableAcrylic, _wallpaper.HasImage);
                resources["EchoFont"] = _fonts.Family(preferences);
                _window.Resources.ThemeDictionaries[toneDark ? ThemeVariant.Dark : ThemeVariant.Light] = resources;
            }
            _setDark(dark); _window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            _rainbow.Apply(); UpdateTimer();
        }
        finally { IsApplying = false; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _timer.Stop(); _timer.Tick -= Tick;
        _window.Activated -= Activated; _window.Deactivated -= Activated; _window.PropertyChanged -= WindowChanged;
        _window.SizeChanged -= Sized; _motion.PropertyChanged -= MotionChanged;
        _theme.Changed -= Apply;
        if (_platform is not null) _platform.ColorValuesChanged -= OnPlatformChanged;
        _theme.WallpaperReloadRequested -= ReloadWallpaper;
        _wallpaper.AvailabilityChanged -= Apply; _wallpaper.Dispose(); _rainbow.Dispose(); _fonts.Dispose();
    }
}

