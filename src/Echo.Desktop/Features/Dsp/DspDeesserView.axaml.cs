using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace Echo.Desktop.Features.Dsp;
public partial class DspDeesserView : UserControl
{
    public DspDeesserView()
    {
        InitializeComponent();
        DeesserListenButton.AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        DeesserListenButton.AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        DeesserListenButton.PointerCaptureLost += (_, _) => Stop();
        DeesserListenButton.LostFocus += (_, _) => Stop();
        DeesserListenButton.KeyDown += async (_, e) => { if (e.Key is Key.Space or Key.Enter && DataContext is DspViewModel m) { e.Handled = true; await m.SetDeesserListenAsync(true); } };
        DeesserListenButton.KeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Stop(); } };
        DetachedFromVisualTree += (_, _) => Stop();
    }
    private async void Pressed(object? sender, PointerPressedEventArgs e)
    { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is DspViewModel model) { e.Pointer.Capture(DeesserListenButton); e.Handled = true; await model.SetDeesserListenAsync(true); } }
    private void Released(object? sender, PointerReleasedEventArgs e) { e.Pointer.Capture(null); Stop(); e.Handled = true; }
    private void Stop() { if (DataContext is DspViewModel m) _ = m.SetDeesserListenAsync(false); }
}
