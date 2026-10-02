using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.History;

public partial class HistoryReviewView : UserControl
{
    public static readonly StyledProperty<int> MetricColumnsProperty = AvaloniaProperty.Register<HistoryReviewView, int>(nameof(MetricColumns), 4);
    public int MetricColumns { get => GetValue(MetricColumnsProperty); set => SetValue(MetricColumnsProperty, value); }
    public HistoryReviewView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 760; MetricColumns = compact ? 2 : 4;
            RankColumns.ColumnDefinitions = new(compact ? "*" : "*,*");
            RankColumns.RowDefinitions = new(compact ? "Auto,Auto" : "Auto");
            Grid.SetColumn(ArtistRanks, compact ? 0 : 1); Grid.SetRow(ArtistRanks, compact ? 1 : 0);
            ArtistRanks.Margin = compact ? new Thickness(0,24,0,0) : default;
        };
        DataContextChanged += (_, _) =>
        {
            Activity.Culture = (DataContext as HistoryReviewDisplay)?.Culture ?? System.Globalization.CultureInfo.InvariantCulture;
            Activity.SetDays((DataContext as HistoryReviewDisplay)?.Data.Days ?? []);
        };
        Activity.OpenDay += date => this.GetVisualAncestors().OfType<HistoryView>().FirstOrDefault()?.Model?.ShowDay(date);
        Activity.SelectionChanged += (day, plays) => ActivitySummary.Text = day.Date + "  ·  " + (plays ? day.Plays.ToString("N0", Activity.Culture) : HistoryText.Clock(day.Seconds));
    }
    private void OnExplore(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: HistoryRankDisplay rank })
            this.GetVisualAncestors().OfType<HistoryView>().FirstOrDefault()?.Model?.ExploreCommand.Execute(rank.Name);
    }
    private void OnMetric(object? sender, RoutedEventArgs args)
    {
        var plays = sender is Button { Tag: "plays" }; Activity.SetMetric(plays);
        TimeMetric.Classes.Set("active", !plays); PlayMetric.Classes.Set("active", plays);
    }
}
