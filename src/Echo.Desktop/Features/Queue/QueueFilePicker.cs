using Avalonia.Controls;
using Avalonia.Platform.Storage;
namespace Echo.Desktop.Features.Queue;
internal static class QueueFilePicker
{
    public static async Task AddAsync(Control owner, QueueViewModel model)
    {
        if (model.IsBusy || TopLevel.GetTopLevel(owner) is not { } top) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "添加到播放队列", AllowMultiple = true,
                FileTypeFilter = [new FilePickerFileType("音频") { Patterns = ["*.mp3", "*.flac", "*.wav", "*.m4a", "*.aac", "*.ogg", "*.opus", "*.aiff", "*.ape", "*.wv", "*.dsf", "*.dff", "*.wma", "*.ac3", "*.mka"] }]
            });
            await model.EnqueuePathsAsync(files.Select(x => x.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception) { model.Message = "无法打开文件选择器，请重新选择。"; }
    }
}
