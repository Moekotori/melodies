using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Queue;
public partial class QueueView : UserControl, IDisposable
{
    private QueuePageViewModel? page;
    public QueueView()
    {
        InitializeComponent();
        DetachedFromVisualTree += (_, _) => Dispose();
        SizeChanged += (_, _) => ApplyLayout();
    }
    public QueueView(PlayerViewModel player, LocalizationService text, bool history) : this()
    { page = new(player, text, history); DataContext = page; }
    private void ApplyLayout()
    {
        var wide = Bounds.Width >= 760;
        HeroLayout.ColumnDefinitions[0].Width = new(wide ? 88 : 64);
        HeroLayout.ColumnDefinitions[2].Width = new(wide ? 228 : 0);
        CurrentCover.Width = CurrentCover.Height = wide ? 88 : 64;
        Grid.SetRow(NextPreview, wide ? 0 : 1);
        Grid.SetColumn(NextPreview, wide ? 2 : 0);
        Grid.SetColumnSpan(NextPreview, wide ? 1 : 3);
        NextPreview.BorderThickness = wide ? new Thickness(1, 0, 0, 0) : new Thickness(0, 1, 0, 0);
        NextPreview.Padding = wide ? new Thickness(16, 0, 0, 0) : new Thickness(0, 10, 0, 0);
        NextPreview.Margin = wide ? new Thickness(0) : new Thickness(0, 12, 0, 0);
        NextPreview.MaxWidth = wide ? 228 : double.PositiveInfinity;
        ListHeading.ColumnDefinitions[1].Width = new(Bounds.Width >= 720 ? 260 : 160);
    }
    private async void OnPlanPlay(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: QueuePlanItem item } && page is not null)
        { args.Handled = true; await page.Queue.PlayPlanAsync(item.ItemId); }
    }
    private async void OnAdd(object? sender, RoutedEventArgs args)
    { if (page is not null) await QueueFilePicker.AddAsync(this, page.Queue); }
    private void OnLocate(object? sender, RoutedEventArgs args) => QueueTracks.LocateCurrent();
    public void FocusSearch() => QueueSearch.Focus();
    public void LocateCurrent() => QueueTracks.LocateCurrent();
    public void Dispose() { page?.Dispose(); page = null; }
}
