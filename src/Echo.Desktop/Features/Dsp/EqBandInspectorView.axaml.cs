using Avalonia.Controls;
namespace Echo.Desktop.Features.Dsp;

public partial class EqBandInspectorView : UserControl
{
    public EqBandInspectorView()
    {
        InitializeComponent(); SizeChanged += (_, _) => ArrangeFields();
    }
    private void ArrangeFields()
    {
        var compact = Bounds.Width < 690;
        InspectorLayout.ColumnDefinitions = new(compact ? "*,*,*" : "112,*,*,*,*");
        InspectorLayout.RowDefinitions = new(compact ? "Auto,Auto" : "Auto");
        BandIdentity.Orientation = compact ? Avalonia.Layout.Orientation.Horizontal : Avalonia.Layout.Orientation.Vertical;
        Grid.SetColumnSpan(BandIdentity, compact ? 2 : 1);
        var fields = new Control[] { FrequencyField, GainField, QField, FilterField };
        for (var i = 0; i < fields.Length; i++)
        {
            Grid.SetColumn(fields[i], compact ? i == 3 ? 2 : i : i + 1);
            Grid.SetRow(fields[i], compact && i < 3 ? 1 : 0);
        }
    }
}
