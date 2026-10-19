using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Dsp;

public partial class DspGenericView : UserControl
{
    public DspGenericView()
    {
        InitializeComponent();
        DeesserListenButton.AddHandler(PointerPressedEvent, ListenPressed, RoutingStrategies.Tunnel);
        DeesserListenButton.AddHandler(PointerReleasedEvent, ListenReleased, RoutingStrategies.Tunnel);
        DeesserListenButton.PointerCaptureLost += (_, _) => StopListen();
        DeesserListenButton.KeyDown += async (_, e) => { if (e.Key is Key.Space or Key.Enter && DataContext is DspViewModel m) { e.Handled = true; await m.SetDeesserListenAsync(true); } };
        DeesserListenButton.KeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter) { e.Handled = true; StopListen(); } };
        DetachedFromVisualTree += (_, _) => StopListen();
        SizeChanged += (_, _) => ArrangeParameters();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(ArrangeParameters, DispatcherPriority.Loaded);
    }
    private async void ListenPressed(object? sender, PointerPressedEventArgs e)
    { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is DspViewModel model) { e.Pointer.Capture(DeesserListenButton); e.Handled = true; await model.SetDeesserListenAsync(true); } }
    private void ListenReleased(object? sender, PointerReleasedEventArgs e) { e.Pointer.Capture(null); StopListen(); e.Handled = true; }
    private void ListenLostFocus(object? sender, RoutedEventArgs e) => StopListen();
    private void StopListen() { if (DataContext is DspViewModel model) _ = model.SetDeesserListenAsync(false); }
    private void ArrangeParameters()
    {
        if (ParametersList.GetVisualDescendants().OfType<UniformGrid>().FirstOrDefault() is { } grid)
            grid.Columns = Bounds.Width < 650 ? 1 : 2;
    }
    private async void ImportRoom(object? sender, RoutedEventArgs e) => await DspPresetDialogs.ImportImpulseAsync(this);
}
