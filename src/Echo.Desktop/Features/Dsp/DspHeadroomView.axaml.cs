using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
namespace Echo.Desktop.Features.Dsp;
public partial class DspHeadroomView : UserControl
{
    public DspHeadroomView()
    {
        InitializeComponent();
        HeadroomListenButton.AddHandler(PointerPressedEvent, Pressed, RoutingStrategies.Tunnel);
        HeadroomListenButton.AddHandler(PointerReleasedEvent, Released, RoutingStrategies.Tunnel);
        HeadroomListenButton.PointerCaptureLost += (_, _) => Stop();
        HeadroomListenButton.LostFocus += (_, _) => Stop();
        HeadroomListenButton.KeyDown += async (_, e) => { if (e.Key is Key.Space or Key.Enter && DataContext is DspViewModel m) { e.Handled = true; await m.SetHeadroomListenAsync(true); } };
        HeadroomListenButton.KeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter) { e.Handled = true; Stop(); } };
        DetachedFromVisualTree += (_, _) => Stop();
    }
    private async void Pressed(object? sender, PointerPressedEventArgs e)
    { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is DspViewModel model) { e.Pointer.Capture(HeadroomListenButton); e.Handled = true; await model.SetHeadroomListenAsync(true); } }
    private void Released(object? sender, PointerReleasedEventArgs e) { e.Pointer.Capture(null); Stop(); e.Handled = true; }
    private void Stop() { if (DataContext is DspViewModel m) _ = m.SetHeadroomListenAsync(false); }
}
