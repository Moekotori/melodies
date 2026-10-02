using Avalonia.Platform.Storage;
using Echo.Contracts.Media;

namespace Echo.Desktop.Features.Player;

internal static class AudioFilePickerTypes
{
    public static IReadOnlyList<FilePickerFileType> All { get; } =
    [new("音频文件 / CUE") { Patterns = AudioFormatCatalog.AudioDialogPatterns },
        new("osu! 谱面音源") { Patterns = ["*.osu"] }, FilePickerFileTypes.All];
}
