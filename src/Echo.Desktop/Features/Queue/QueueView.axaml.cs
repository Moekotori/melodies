using Avalonia.Controls;
using Avalonia.Interactivity;
using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Features.Queue;
public partial class QueueView : UserControl
{
    public QueueView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var wide = Bounds.Width >= 850;
            HeroLayout.ColumnDefinitions[0].Width = new GridLength(wide ? 160 : 96);
            HeroLayout.ColumnDefinitions[2].Width = new GridLength(wide ? 310 : 0);
            NextPreview.IsVisible = wide;
            CurrentCover.Width = CurrentCover.Height = wide ? 160 : 96;
        };
    }
    private async void OnAdd(object? sender, RoutedEventArgs args)
    { if (DataContext is PlayerViewModel player) await QueueFilePicker.AddAsync(this, player.Queue); }
    private void OnLocate(object? sender, RoutedEventArgs args) => QueueTracks.LocateCurrent();
}
