using System.Globalization;
using Avalonia.Data.Converters;
using Echo.Playback;

namespace Echo.Desktop.Features.Settings.Drawers;

public sealed class OutputDevicePresentation : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not AudioOutputDevice device) return "";
        var mode = device.Mode switch { "shared" => "共享", "exclusive" => "独占", "asio" => "ASIO", "ks" => "WDM-KS", _ => device.Mode };
        return parameter?.ToString() switch
        {
            "icon" => device.Mode == "asio" ? "headphones" : "monitor",
            "badge" => mode,
            "detail" => device.Id.Length == 0 ? "系统选择的路径" : device.SampleRate > 0 ? $"{mode} / {device.SampleRate / 1000d:0.#} kHz" : $"{mode} / 播放时确认格式",
            _ => device.Id.Length == 0 ? "系统默认" : device.Name
        };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
