using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Player;

public partial class PlayerOutputPanel : UserControl
{
    public PlayerOutputPanel() => InitializeComponent();
    private async void OpenFiles(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not ShellViewModel shell || shell.Player.IsBusy || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            { Title = "打开音频文件", AllowMultiple = true, FileTypeFilter = AudioFilePickerTypes.All });
            await shell.Player.OpenPathsAsync(files.Select(x => x.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception) { shell.Player.Error = "无法打开文件，请重新选择。"; }
    }
}
