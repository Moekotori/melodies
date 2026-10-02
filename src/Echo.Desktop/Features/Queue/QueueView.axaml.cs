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
        var wide = Bounds.Width >= 900;
        HeroLayout.ColumnDefinitions[0].Width = new(wide ? 176 : 100);
        HeroLayout.ColumnDefinitions[2].Width = new(wide ? 300 : 0);
        HeroLayout.MinHeight = wide ? 176 : 120;
        NextPreview.IsVisible = wide;
        CurrentCover.Width = CurrentCover.Height = wide ? 176 : 100;
        PlaybackActions.IsVisible = Bounds.Width >= 750;
        ListHeading.ColumnDefinitions[1].Width = new(Bounds.Width >= 750 ? 260 : 180);
    }
    private async void OnAdd(object? sender, RoutedEventArgs args)
    { if (page is not null) await QueueFilePicker.AddAsync(this, page.Queue); }
    private void OnLocate(object? sender, RoutedEventArgs args) => QueueTracks.LocateCurrent();
    public void FocusSearch() => QueueSearch.Focus();
    public void LocateCurrent() => QueueTracks.LocateCurrent();
    public void Dispose() { page?.Dispose(); page = null; }
}
