using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Home;

public partial class ListeningOverview : UserControl
{
    public ListeningOverview() => InitializeComponent();
    public void RefreshHeatmap()
    {
        if (DataContext is not HomeViewModel model) return;
        var today = DateOnly.FromDateTime(DateTime.Now); Heatmap.SetDays(model.Days, today);
        var first = today.AddDays(-(((int)today.DayOfWeek + 6) % 7) - 77);
        MonthLabels.Text = string.Join("          ", Enumerable.Range(0, 12).Select(week => first.AddDays(week * 7))
            .DistinctBy(date => (date.Year, date.Month)).Select(date => date.ToString("MMM", model.Shell.Text.Current.Culture)));
    }
}
