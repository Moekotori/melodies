using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Echo.Desktop.Features.Floating;

public partial class DesktopLyricsSettingsView : UserControl
{
    public DesktopLyricsSettingsView() => InitializeComponent();
    private async void ImportFont(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || DataContext is not FloatingWindowsViewModel model) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new("Font") { Patterns = ["*.ttf", "*.otf", "*.ttc", "*.woff"] }]
        });
        var path = files.Count == 0 ? null : files[0].TryGetLocalPath();
        if (path is { Length: > 0 and <= 1024 }) model.FontFilePath = path;
    }
}
