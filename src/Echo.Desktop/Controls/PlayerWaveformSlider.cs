using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Echo.Desktop.Controls;

// Native Slider keeps keyboard/automation semantics; the whole waveform is its pointer hit area.
public sealed class PlayerWaveformSlider : Slider
{
    public static readonly StyledProperty<Echo.Contracts.Media.TrackEnvelope?> EnergyEnvelopeProperty = AvaloniaProperty.Register<PlayerWaveformSlider, Echo.Contracts.Media.TrackEnvelope?>(nameof(EnergyEnvelope));
    public static readonly StyledProperty<bool> IsWaveformVisibleProperty = AvaloniaProperty.Register<PlayerWaveformSlider, bool>(nameof(IsWaveformVisible), true);
    public Echo.Contracts.Media.TrackEnvelope? EnergyEnvelope { get => GetValue(EnergyEnvelopeProperty); set => SetValue(EnergyEnvelopeProperty, value); }
    public bool IsWaveformVisible { get => GetValue(IsWaveformVisibleProperty); private set => SetValue(IsWaveformVisibleProperty, value); }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == EnergyEnvelopeProperty) IsWaveformVisible = EnergyEnvelope is null; }
    public static readonly StyledProperty<string> SeedProperty = AvaloniaProperty.Register<PlayerWaveformSlider, string>(nameof(Seed), "idle");
    public string Seed { get => GetValue(SeedProperty); set => SetValue(SeedProperty, value); }
    private IPointer? dragging;
    public PlayerWaveformSlider()
    {
        LostFocus += (_, _) => dragging?.Capture(null);
        DetachedFromVisualTree += (_, _) => dragging?.Capture(null);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { base.OnPointerPressed(e); return; }
        Focus(); dragging = e.Pointer; e.Pointer.Capture(this); Preview(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    { if (dragging == e.Pointer) { Preview(e); e.Handled = true; } else base.OnPointerMoved(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (dragging != e.Pointer) { base.OnPointerReleased(e); return; }
        Preview(e); dragging = null; e.Pointer.Capture(null); e.Handled = true;
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { dragging = null; base.OnPointerCaptureLost(e); }
    private void Preview(PointerEventArgs e)
    {
        if (Bounds.Width <= 0) return;
        var ratio = Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
        SetCurrentValue(ValueProperty, Minimum + ratio * (Maximum - Minimum));
    }
}
