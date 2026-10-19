using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsTransportView
{
    public void Fit(double width)
    {
        var mini = Model?.Appearance.MiniPlayer != false;
        var barWidth = mini ? Math.Max(280, Math.Min(width - (width >= 1050 ? 108 : 48), 740)) : Math.Max(280, width);
        Classes.Set("docked", !mini);
        if (mini && QueueButton.Parent == AuxiliaryControls)
        { AuxiliaryControls.Children.Remove(QueueButton); PlaybackControls.Children.Insert(5, QueueButton); }
        else if (!mini && QueueButton.Parent == PlaybackControls)
        { PlaybackControls.Children.Remove(QueueButton); AuxiliaryControls.Children.Insert(0, QueueButton); }
        HorizontalAlignment = mini ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        CapsuleSurface.IsVisible = mini;
        ExpandedLayout.Clip = mini ? contentClip : null;
        CenterLayout.VerticalAlignment = VerticalAlignment.Center;
        CenterLayout.MaxWidth = mini ? double.PositiveInfinity : 880;
        CenterLayout.Margin = mini ? default : new Thickness(0, 0, 24, 0);
        TransportLayout.Width = barWidth;
        TransportLayout.Height = BarSurface.Height = mini ? 52 : width < 640 ? 76 : 58;
        IdleSurface.Width = Math.Min(320, barWidth);
        var tiny = barWidth < 500;
        ModeButton.IsVisible = barWidth >= (mini ? 640 : 500);
        LyricsButton.IsVisible = mini && barWidth >= 640;
        QueueButton.IsVisible = barWidth >= 500;
        FavoriteButton.IsVisible = !tiny;
        foreach (var button in PlaybackControls.Children.OfType<Button>().Concat(AuxiliaryControls.Children.OfType<Button>()))
            button.Width = button.Height = tiny ? 30 : 32;
        PlaybackControls.Spacing = tiny ? 4 : mini ? 6 : 10;
        AuxiliaryControls.Spacing = 8;
        BarSurface.Padding = new Thickness(mini ? tiny ? 10 : 16 : Math.Clamp(width * .04, 16, 56), 0);
        ProgressRow.Margin = new Thickness(tiny ? 8 : 16, 0, tiny ? 8 : 20, 0);
        ProgressRow.ColumnSpacing = tiny ? 6 : 10;
        ProgressRow.ColumnDefinitions[0].Width = new GridLength(tiny ? 32 : 42);
        ProgressRow.ColumnDefinitions[2].Width = new GridLength(tiny ? 0 : 42);
        CenterLayout.RowDefinitions.Clear();
        var stacked = !mini && width < 640;
        CenterLayout.ColumnDefinitions[0].Width = stacked ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        CenterLayout.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        CenterLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (stacked) CenterLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(ProgressRow, stacked ? 1 : 0);
        Grid.SetColumn(ProgressRow, stacked ? 0 : 1);
        PlaybackControls.HorizontalAlignment = stacked ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        if (stacked) ProgressRow.Margin = default;
        DurationLabel.IsVisible = !tiny || !mini;
        if (!mini)
        {
            ProgressRow.ColumnDefinitions[0].Width = ProgressRow.ColumnDefinitions[2].Width = new GridLength(34);
            ProgressRow.ColumnSpacing = 10;
        }
        ElapsedText.FontSize = DurationLabel.FontSize = mini ? 12 : 11;
        ApplyPresentation();
    }
}
