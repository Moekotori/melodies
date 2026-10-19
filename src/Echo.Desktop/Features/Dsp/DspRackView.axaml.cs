using Avalonia.Controls;
namespace Echo.Desktop.Features.Dsp;
public sealed partial class DspRackView : UserControl
{
    public DspRackView()
    {
        InitializeComponent(); SizeChanged += (_, _) => ArrangeRack();
    }
    private void ArrangeRack()
    {
        var narrow = Bounds.Width < 730;
        RackLayout.ColumnDefinitions = new(narrow ? "*" : "280,*");
        RackLayout.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        Grid.SetColumn(RackDetails, narrow ? 0 : 1); Grid.SetRow(RackDetails, narrow ? 1 : 0);
    }
}
