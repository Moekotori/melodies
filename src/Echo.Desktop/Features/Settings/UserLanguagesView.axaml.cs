using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Echo.Desktop.Localization;

namespace Echo.Desktop.Features.Settings;

public partial class UserLanguagesView : UserControl
{
    private bool _picking;
    public UserLanguagesView() => InitializeComponent();
    private async void OnImport(object? sender, RoutedEventArgs args)
    {
        if (_picking || DataContext is not UserLanguagesViewModel { CanRun: true } model || TopLevel.GetTopLevel(this) is not { } top) return;
        _picking = true;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new() { Title = Caption("language.user.import"), AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
            if (!this.IsAttachedToVisualTree() || files.Count == 0) return;
            await using var source = await files[0].OpenReadAsync();
            await model.ImportAsync(source);
        }
        catch (Exception error) when (UserLanguageStore.IsPackError(error)) { model.ReportError(error); }
        finally { _picking = false; }
    }
    private async void OnExport(object? sender, RoutedEventArgs args)
    {
        if (_picking || DataContext is not UserLanguagesViewModel { CanRun: true } model || TopLevel.GetTopLevel(this) is not { } top) return;
        _picking = true;
        try
        {
            var file = await top.StorageProvider.SaveFilePickerAsync(new() { Title = Caption("language.user.export"), SuggestedFileName = "echo-language.json",
                DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }] });
            if (!this.IsAttachedToVisualTree() || file is null) return;
            await using var destination = await file.OpenWriteAsync();
            await model.ExportAsync(destination);
        }
        catch (Exception error) when (UserLanguageStore.IsPackError(error)) { model.ReportError(error); }
        finally { _picking = false; }
    }
    private async void OnOpenDirectory(object? sender, RoutedEventArgs args)
    {
        if (DataContext is UserLanguagesViewModel model && TopLevel.GetTopLevel(this) is { } top)
            await model.OpenDirectoryAsync(path => top.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)));
    }
    private string Caption(string key) => this.TryFindResource(key, out var value) ? value?.ToString() ?? key : key;
}
