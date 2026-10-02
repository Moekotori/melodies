using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;

namespace Echo.Desktop.Theming;

// One static HSV surface; captures only its own pointer and owns no timer or bitmap.
public sealed class ThemeSpectrum : Control
{
    public static readonly StyledProperty<double> HueProperty = AvaloniaProperty.Register<ThemeSpectrum, double>(nameof(Hue));
    public static readonly StyledProperty<double> SaturationProperty = AvaloniaProperty.Register<ThemeSpectrum, double>(nameof(Saturation), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> BrightnessProperty = AvaloniaProperty.Register<ThemeSpectrum, double>(nameof(Brightness), defaultBindingMode: BindingMode.TwoWay);
    private static readonly LinearGradientBrush WhiteLayer = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = new() { new(Colors.White, 0), new(Color.FromArgb(0, 255, 255, 255), 1) }
    };
    private static readonly LinearGradientBrush BlackLayer = new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = new() { new(Colors.Transparent, 0), new(Colors.Black, 1) }
    };
    public double Hue { get => GetValue(HueProperty); set => SetValue(HueProperty, value); }
    public double Saturation { get => GetValue(SaturationProperty); set => SetValue(SaturationProperty, value); }
    public double Brightness { get => GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    static ThemeSpectrum() => AffectsRender<ThemeSpectrum>(HueProperty, SaturationProperty, BrightnessProperty);
    public ThemeSpectrum()
    { Focusable = true; Cursor = new Cursor(StandardCursorType.Cross); DetachedFromVisualTree += (_, _) => { if (_pointer?.Captured == this) _pointer.Capture(null); _pointer = null; }; }
    private IPointer? _pointer;
    public override void Render(DrawingContext context)
    {
        var rectangle = new Rect(Bounds.Size);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;
        using (context.PushClip(new RoundedRect(rectangle, 4)))
        {
            context.DrawRectangle(new SolidColorBrush(ThemeColorMath.FromHsv(Hue, 1, 1)), null, rectangle);
            context.DrawRectangle(WhiteLayer, null, rectangle); context.DrawRectangle(BlackLayer, null, rectangle);
        }
        var point = new Point(Math.Clamp(Saturation * Bounds.Width, 6, Math.Max(6, Bounds.Width - 6)),
            Math.Clamp((1 - Brightness) * Bounds.Height, 6, Math.Max(6, Bounds.Height - 6)));
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Colors.Black, .45), 3), point, 5, 5);
        context.DrawEllipse(null, new Pen(Brushes.White, 1.8), point, 5, 5);
        if (IsKeyboardFocusWithin) context.DrawRectangle(null, new Pen(Brushes.White, 1), rectangle.Deflate(1), 4, 4);
    }
    private void Select(Point point)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        SetCurrentValue(SaturationProperty, Math.Clamp(point.X / Bounds.Width, 0, 1));
        SetCurrentValue(BrightnessProperty, Math.Clamp(1 - point.Y / Bounds.Height, 0, 1));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus(); _pointer = e.Pointer; e.Pointer.Capture(this); Select(e.GetPosition(this)); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    { base.OnPointerMoved(e); if (e.Pointer.Captured == this) { Select(e.GetPosition(this)); e.Handled = true; } }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (e.Pointer.Captured == this) { e.Pointer.Capture(null); _pointer = null; e.Handled = true; } }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { if (_pointer == e.Pointer) _pointer = null; base.OnPointerCaptureLost(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? .1 : .01;
        switch (e.Key)
        {
            case Key.Left: SetCurrentValue(SaturationProperty, Math.Max(0, Saturation - step)); break;
            case Key.Right: SetCurrentValue(SaturationProperty, Math.Min(1, Saturation + step)); break;
            case Key.Up: SetCurrentValue(BrightnessProperty, Math.Min(1, Brightness + step)); break;
            case Key.Down: SetCurrentValue(BrightnessProperty, Math.Max(0, Brightness - step)); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
    }
}
