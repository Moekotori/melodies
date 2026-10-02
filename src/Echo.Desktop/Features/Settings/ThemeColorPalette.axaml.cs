using Avalonia.Controls;

namespace Echo.Desktop.Features.Settings;

public partial class ThemeColorPalette : UserControl
{
    public ThemeColorPalette()
    {
        InitializeComponent(); SizeChanged += (_, _) =>
        {
            var narrow = Bounds.Width < 570;
            ColorWorkspace.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "*,220");
            ColorWorkspace.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "*");
            Grid.SetColumn(InspectorBorder, narrow ? 0 : 1); Grid.SetRow(InspectorBorder, narrow ? 1 : 0);
            InspectorBorder.BorderThickness = narrow ? new Avalonia.Thickness(0,1,0,0) : new Avalonia.Thickness(1,0,0,0);
            InspectorBorder.Padding = narrow ? new Avalonia.Thickness(0,18,0,0) : new Avalonia.Thickness(24,0,0,0);
        };
    }
}
