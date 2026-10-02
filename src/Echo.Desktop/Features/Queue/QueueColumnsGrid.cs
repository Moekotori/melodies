using Avalonia;
using Avalonia.Controls;
namespace Echo.Desktop.Features.Queue;
public sealed class QueueColumnsGrid : Grid
{
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<QueueColumnsGrid, bool>(nameof(Compact));
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    public static readonly StyledProperty<bool> DrawerProperty = AvaloniaProperty.Register<QueueColumnsGrid, bool>(nameof(Drawer));
    public bool Drawer { get => GetValue(DrawerProperty); set => SetValue(DrawerProperty, value); }
    private bool narrow;
    public QueueColumnsGrid()
    {
        Apply();
        SizeChanged += (_, _) =>
        {
            var value = Bounds.Width < 340;
            if (narrow == value) return;
            narrow = value;
            if (Drawer) Apply();
        };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == CompactProperty || change.Property == DrawerProperty) Apply(); }
    private void Apply()
    { ColumnDefinitions = new(Drawer ? narrow ? "14,0,40,*,0,0,34,24" : "14,0,40,*,0,64,34,24" : Compact ? "0,0,40,*,0,0,0,64" : "18,24,48,*,150,120,58,64"); ColumnSpacing = Compact || Drawer ? 6 : 11; }
}
