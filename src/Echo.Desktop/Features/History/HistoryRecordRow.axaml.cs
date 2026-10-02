using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.History;

public partial class HistoryRecordRow : UserControl
{
    public HistoryRecordRow()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 780;
            TrackColumns.ColumnDefinitions = new(compact ? "64,3*,0,140,28" : "64,3*,2*,150,28");
            AlbumName.IsVisible = !compact;
        };
    }
    private void OnDetails(object? sender, RoutedEventArgs args)
    { if (DataContext is HistoryRow row) row.Expanded = !row.Expanded; args.Handled = true; }
}
public sealed class HistoryAvailabilityConverter : IValueConverter
{
    public static HistoryAvailabilityConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? 1d : .5d;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
