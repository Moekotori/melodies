using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Echo.Desktop.Features.Dsp;

public partial class DspHeadphoneView : UserControl
{
    public DspHeadphoneView() => InitializeComponent();

    private async void ImportCurve(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage || DataContext is not DspViewModel model) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入耳机校正曲线", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("曲线") { Patterns = ["*.csv", "*.txt"] }],
        });
        if (files.Count == 0) return;
        model.ImportHeadphoneCurve(files[0].TryGetLocalPath() ?? "");
    }
}
