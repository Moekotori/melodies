using System.Text;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Echo.Desktop.Features.Dsp;

internal static class DspPresetDialogs
{
    public static async Task ImportAsync(Control control)
    {
        if (control.DataContext is not DspViewModel model || TopLevel.GetTopLevel(control)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new() { Title = "导入参数预设", AllowMultiple = false, FileTypeFilter = [new("EQ / REW / APO / 频响曲线") { Patterns = ["*.json", "*.txt", "*.csv"] }] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await model.ImportPresetAsync(path);
    }
    public static async Task ExportAsync(Control control)
    {
        if (control.DataContext is not DspViewModel model || TopLevel.GetTopLevel(control)?.StorageProvider is not { } storage) return;
        try
        {
            var json = model.ExportCurrentPreset();
            var file = await storage.SaveFilePickerAsync(new() { Title = "导出均衡器预设", SuggestedFileName = "ECHO-EQ.json", DefaultExtension = "json", FileTypeChoices = [new("EQ JSON") { Patterns = ["*.json"] }] });
            if (file is null) return;
            await using var output = await file.OpenWriteAsync(); output.SetLength(0); await output.WriteAsync(Encoding.UTF8.GetBytes(json));
            model.Message = "预设已导出，作者与授权信息一并保留。";
        }
        catch (Exception error) { model.Message = "导出失败：" + error.Message; }
    }
    public static async Task ImportImpulseAsync(Control control)
    {
        if (control.DataContext is not DspViewModel model || TopLevel.GetTopLevel(control)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new() { Title = "导入脉冲或多采样率资源包", AllowMultiple = false, FileTypeFilter = [new("脉冲响应") { Patterns = ["*.wav", "*.flac", "*.aif", "*.aiff", "*.zip"] }] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await model.ImportImpulseBankAsync(path);
    }
}
