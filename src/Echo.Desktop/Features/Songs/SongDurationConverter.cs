using System.Globalization;
using Avalonia.Data.Converters;
namespace Echo.Desktop.Features.Songs;

public sealed class SongDurationConverter : IValueConverter
{
    public static SongDurationConverter Instance { get; } = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var seconds = value is double duration && double.IsFinite(duration) ? Math.Clamp(duration, 0, 315360000) : 0;
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? $"{(long)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(long)time.TotalMinutes}:{time.Seconds:00}";
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
