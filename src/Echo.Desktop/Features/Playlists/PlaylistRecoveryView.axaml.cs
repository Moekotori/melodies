using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Echo.Contracts.Library;

namespace Echo.Desktop.Features.Playlists;

public partial class PlaylistRecoveryView : UserControl, IDisposable
{
    public event Action? Close;
    public PlaylistRecoveryView() => InitializeComponent();
    public PlaylistRecoveryView(PlaylistRecoveryViewModel model) : this()
    { DataContext = model; AttachedToVisualTree += (_, _) => _ = model.LoadAsync(); }
    private void OnClose(object? sender, RoutedEventArgs args) => Close?.Invoke();
    private void OnKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Escape) { Close?.Invoke(); args.Handled = true; } }
    private async void OnRestore(object? sender, RoutedEventArgs args)
    { if (sender is Button { DataContext: PlaylistRow row } && DataContext is PlaylistRecoveryViewModel model) await model.RestoreAsync(row); }
    public void Dispose() { (DataContext as IDisposable)?.Dispose(); DataContext = null; Close = null; }
}
