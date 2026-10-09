using Echo.Desktop.Motion;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Contracts.Settings;
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
    private bool? _acrylicHint;
    private PaletteStamp? _palette;
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
            if (_acrylicHint != useAcrylic)
            {
                _window.TransparencyLevelHint = useAcrylic ? [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.None] : [WindowTransparencyLevel.None];
                _acrylicHint = useAcrylic;
            }
            var availableAcrylic = useAcrylic && _window.ActualTransparencyLevel != WindowTransparencyLevel.None;
            _theme.AcrylicStatus = !requestedAcrylic ? "" : !useAcrylic ? "窗口失焦时使用实色" : availableAcrylic ? "系统模糊已启用" : "当前平台未提供系统模糊，已使用实色背景";
            var tone = _theme.Tone(dark);
            _motion.ThemeAllowsAnimation = tone.MotionEnabled;
            _motion.ThemeDurationScale = tone.Numbers["motionSpeedSeconds"] / .22;
            _motion.ThemeIntensityScale = tone.Numbers["motionIntensityPercent"] / 100;
            var appearance = preferences.Appearance ?? new();
            _fonts.Apply(appearance); _wallpaper.Apply(preferences, tone, _motion.UseSpatialMotion);
            var font = _fonts.Family(preferences);
            var stamp = PaletteStamp.Capture(preferences, appearance, dark, availableAcrylic, _wallpaper.HasImage, system?.AccentColor1, font.ToString());
            // Brightness, blur and wallpaper framing do not change brushes. Skip the dictionary rebuild while a slider is moving.
            if (_palette != stamp)
            {
                foreach (var toneDark in new[] { false, true })
                {
                    var resources = ThemeResourcePalette.Create(_theme.Tone(toneDark), preferences, toneDark, system?.AccentColor1, availableAcrylic, _wallpaper.HasImage);
                    resources["EchoFont"] = font;
                    _window.Resources.ThemeDictionaries[toneDark ? ThemeVariant.Dark : ThemeVariant.Light] = resources;
                }
                _palette = stamp;
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
    private readonly record struct PaletteStamp(bool Dark, bool Acrylic, bool Image, byte AccentA, byte AccentR, byte AccentG, byte AccentB, bool HasAccent,
        string Mode, string Font, string ChineseFont, string FallbackFont, bool AcrylicEnabled, double AcrylicTransparency, bool HasWallpaper,
        bool WallpaperEnabled, double UiOpacity, bool Unified, bool Protection, string? PageLight, string? PageDark, string Cover, bool Portrait,
        ThemeTone? Light, ThemeTone? DarkTone, ImportedFont? MainFont, ImportedFont? ChineseFile, ImportedFont? FallbackFile, string Family)
    {
        public static PaletteStamp Capture(ThemePreferences preferences, AppearancePreferences appearance, bool dark, bool acrylic, bool image, Color? accent, string family)
        {
            var color = accent.GetValueOrDefault();
            return new(dark, acrylic, image, color.A, color.R, color.G, color.B, accent is not null, preferences.Mode, preferences.Font,
                preferences.ChineseFont, preferences.FallbackFont, preferences.AcrylicEnabled, preferences.AcrylicTransparency, preferences.Wallpaper.Length > 0,
                appearance.WallpaperEnabled, appearance.WallpaperUiOpacity, appearance.WallpaperUnifiedOpacity, appearance.WallpaperVisualProtection,
                appearance.PageTextLight, appearance.PageTextDark, appearance.CoverShape, appearance.PortraitWallpaper.Length > 0, preferences.Light, preferences.Dark,
                appearance.MainFontFile, appearance.ChineseFontFile, appearance.FallbackFontFile, family);
        }
    }
}

