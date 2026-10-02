using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Desktop.Motion;
using Echo.Desktop.Theming;

namespace Echo.Desktop.Bootstrap;

internal sealed class ThemeRainbowBinding : IDisposable
{
    private readonly Window _window;
    private readonly ThemeViewModel _theme;
    private readonly MotionPolicy _motion;
    private readonly RainbowBackdrop _view;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000d / 15) };
    private long _last = Stopwatch.GetTimestamp();
    private int _accentTick;
    public ThemeRainbowBinding(Window window, ThemeViewModel theme, MotionPolicy motion)
    {
        _window = window; _theme = theme; _motion = motion; _view = window.FindControl<RainbowBackdrop>("ThemeRainbowLayer")!;
        _timer.Tick += Tick; _motion.PropertyChanged += MotionChanged;
    }
    private void MotionChanged(object? s, PropertyChangedEventArgs e) { if (e.PropertyName is nameof(MotionPolicy.CanAnimate) or nameof(MotionPolicy.UseSpatialMotion)) Apply(); }
    public void Apply()
    {
        _view.IsVisible = _theme.RainbowEnabled; _view.Style = _theme.RainbowStyle; _view.Intensity = _theme.RainbowIntensity / 100;
        _view.Dark = _window.RequestedThemeVariant == ThemeVariant.Dark; _view.InvalidateVisual();
        _last = Stopwatch.GetTimestamp();
        if (_theme.RainbowEnabled && _motion.UseSpatialMotion && _window.IsVisible && _window.WindowState != WindowState.Minimized) _timer.Start(); else _timer.Stop();
    }
    private void Tick(object? s, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp(); var delta = Math.Min(.2, Stopwatch.GetElapsedTime(_last, now).TotalSeconds); _last = now;
        _view.Phase = (_view.Phase + delta / 32 * _theme.RainbowSpeed / 100) % 1;
        if (_view.Style != "accents") _view.InvalidateVisual();
        if (++_accentTick % 15 != 0) return;
        var color = ThemeColorMath.FromHsv(_view.Phase * 360, .7, _view.Dark ? .9 : .68);
        // Updating only accent resources avoids rebuilding dictionaries or restarting transitions every frame.
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            if (_window.Resources.ThemeDictionaries.TryGetValue(variant, out var resources) && resources is Avalonia.Controls.ResourceDictionary dictionary)
                foreach (var key in new[] { "EchoAccent", "EchoAccentText", "EchoIconAccent" }) dictionary[key] = new SolidColorBrush(color);
    }
    public void Dispose() { _timer.Stop(); _timer.Tick -= Tick; _motion.PropertyChanged -= MotionChanged; }
}
