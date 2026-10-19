using Avalonia;
using Avalonia.Controls;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Features.Floating;

internal static class DesktopLyricsPlacement
{
    public static void Apply(Window window, Window main, FloatingWindowsViewModel settings, FloatingBounds? saved)
    {
        var screen = saved is not null ? main.Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(new PixelPoint(saved.X + 24, saved.Y + 24))) : null;
        var hasSavedScreen = screen is not null;
        screen ??= main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary;
        if (screen is null) { window.Position = new(main.Position.X + 80, main.Position.Y + 80); return; }
        var area = screen.WorkingArea;
        var scale = Math.Max(1, screen.Scaling);
        var dipW = area.Width / scale;
        var dipH = area.Height / scale;
        var vertical = settings.TextDirection == "vertical";
        var strip = DesktopLyricsLayout.Strip(settings.FontSize, settings.SecondarySize, settings.ScalePercent, settings.DoubleLine);
        var minW = vertical ? 84 : 420;
        var minH = vertical ? 420 : 84;
        var maxW = Math.Max(minW, vertical ? Math.Min(strip + DesktopLyricsLayout.Slack, dipW) : dipW);
        var maxH = Math.Max(minH, vertical ? dipH : Math.Min(strip + DesktopLyricsLayout.Slack, dipH));
        double width, height;
        if (!hasSavedScreen || saved is null)
        {
            width = vertical ? Math.Min(strip, Math.Max(minW, dipW - DesktopLyricsLayout.HorizontalMargin)) : Math.Max(minW, dipW - DesktopLyricsLayout.HorizontalMargin);
            height = vertical ? Math.Max(minH, dipH - DesktopLyricsLayout.VerticalMargin) : strip;
        }
        else
        {
            width = double.IsFinite(saved.Width) ? saved.Width : maxW;
            height = double.IsFinite(saved.Height) ? saved.Height : strip;
            if (vertical)
            {
                width = Math.Max(width, Math.Min(strip, dipW));
                height = Math.Max(height, Math.Min(DesktopLyricsLayout.ReadableHeight, dipH));
            }
            else
            {
                width = Math.Max(width, Math.Min(DesktopLyricsLayout.ReadableWidth, dipW));
                height = Math.Max(height, Math.Min(strip, dipH));
            }
        }
        width = Math.Clamp(width, minW, maxW);
        height = Math.Clamp(height, minH, maxH);
        window.MinWidth = Math.Min(window.MinWidth, minW);
        window.MinHeight = Math.Min(window.MinHeight, minH);
        window.MaxWidth = Math.Max(window.MaxWidth, maxW);
        window.MaxHeight = Math.Max(window.MaxHeight, maxH);
        window.Width = width;
        window.Height = height;
        window.MinWidth = minW;
        window.MinHeight = minH;
        window.MaxWidth = maxW;
        window.MaxHeight = maxH;
        var pixelW = (int)Math.Ceiling(window.Width * scale);
        var pixelH = (int)Math.Ceiling(window.Height * scale);
        var x = hasSavedScreen ? saved!.X : area.X + Math.Max(0, (area.Width - pixelW) / 2);
        var y = hasSavedScreen ? saved!.Y : (int)Math.Round(Math.Max(area.Y, area.Y + area.Height - pixelH - DesktopLyricsLayout.Clearance * scale));
        var minVisibleW = Math.Min(96, pixelW);
        var minVisibleH = Math.Min(48, pixelH);
        window.Position = new(Math.Clamp(x, area.X + minVisibleW - pixelW, Math.Max(area.X, area.Right - minVisibleW)),
            Math.Clamp(y, area.Y + minVisibleH - pixelH, Math.Max(area.Y, area.Bottom - minVisibleH)));
    }
}
