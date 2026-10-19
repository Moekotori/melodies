using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Echo.Desktop.Features.Floating;

public partial class DesktopLyricsWindow
{
    private bool? appliedVertical;
    private void ApplyAxis()
    {
        var vertical = shell.Floating.IsVertical;
        if (appliedVertical == vertical) return;
        appliedVertical = vertical;

        if (vertical)
        {
            Root.RowDefinitions = new RowDefinitions("*");
            Root.ColumnDefinitions = new ColumnDefinitions("48,*");
            ToolbarButtons.Orientation = Orientation.Vertical;
            Toolbar.Width = 48;
            Toolbar.Height = double.NaN;
            SetCell(Toolbar, 0, 0);
            SetCell(LyricsCanvas, 0, 1);
            LyricsCanvas.Margin = new Thickness(0, 12, 12, 12);
            UnlockButton.HorizontalAlignment = HorizontalAlignment.Left;
            UnlockButton.VerticalAlignment = VerticalAlignment.Center;
            UnlockButton.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetRow(UnlockButton, 0);
            Grid.SetRowSpan(UnlockButton, 1);
            Grid.SetColumn(UnlockButton, 0);
            Grid.SetColumnSpan(UnlockButton, 2);
        }
        else
        {
            Root.ColumnDefinitions = new ColumnDefinitions("*");
            Root.RowDefinitions = new RowDefinitions("48,*");
            ToolbarButtons.Orientation = Orientation.Horizontal;
            Toolbar.Width = double.NaN;
            Toolbar.Height = 48;
            SetCell(Toolbar, 0, 0);
            SetCell(LyricsCanvas, 1, 0);
            LyricsCanvas.Margin = new Thickness(12, 0, 12, 8);
            UnlockButton.HorizontalAlignment = HorizontalAlignment.Center;
            UnlockButton.VerticalAlignment = VerticalAlignment.Top;
            UnlockButton.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRowSpan(UnlockButton, 2);
            Grid.SetColumnSpan(UnlockButton, 1);
        }
        Grid.SetRowSpan(Scrim, vertical ? 1 : 2);
        Grid.SetColumnSpan(Scrim, vertical ? 2 : 1);
        OffsetReadout.MinWidth = vertical ? 0 : 46;
        OffsetReadout.FontSize = vertical ? 10 : 12;
        ToolbarCapsule.Padding = vertical ? new Thickness(4, 6) : new Thickness(6, 4);
        foreach (var divider in ToolbarButtons.Children.OfType<Border>())
        {
            divider.Width = vertical ? 16 : 1;
            divider.Height = vertical ? 1 : 16;
            divider.Margin = vertical ? new Thickness(0, 4) : new Thickness(4, 0);
        }
        foreach (var grip in new[] { StartGrip, EndGrip })
        {
            SetCell(grip, 0, 0);
            Grid.SetRowSpan(grip, vertical ? 1 : 2);
            Grid.SetColumnSpan(grip, vertical ? 2 : 1);
            grip.Width = vertical ? double.NaN : 14;
            grip.Height = vertical ? 14 : double.NaN;
            grip.HorizontalAlignment = vertical ? HorizontalAlignment.Stretch : grip == StartGrip ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            grip.VerticalAlignment = vertical ? grip == StartGrip ? VerticalAlignment.Top : VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            grip.Cursor = new Cursor(vertical ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast);
        }
        foreach (var mark in new[] { StartGripMark, EndGripMark }) { mark.Width = vertical ? 30 : 4; mark.Height = vertical ? 4 : 30; }
    }
    private static void SetCell(Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetRowSpan(control, 1);
        Grid.SetColumnSpan(control, 1);
    }
}
