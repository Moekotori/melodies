using Avalonia;
using Avalonia.Controls;
namespace Echo.Desktop.Features.Queue;
public sealed class QueueColumnsGrid : Grid
{
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<QueueColumnsGrid, bool>(nameof(Compact));
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    public QueueColumnsGrid() => Apply();
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    { base.OnPropertyChanged(change); if (change.Property == CompactProperty) Apply(); }
    private void Apply()
    { ColumnDefinitions = new(Compact ? "0,40,*,0,0,0,32" : "24,48,*,160,100,64,36"); ColumnSpacing = Compact ? 8 : 12; }
}
