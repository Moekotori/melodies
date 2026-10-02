using Avalonia;
using Avalonia.Controls;
using Echo.Contracts.Settings;

namespace Echo.Desktop.Features.Floating;

internal static class FloatingWindowPlacement
{
    public static void Place(Window window, Window main, FloatingBounds? saved, bool mini)
    {
        var screen = saved is not null ? main.Screens.All.FirstOrDefault(s => s.WorkingArea.Contains(new PixelPoint(saved.X + 24, saved.Y + 24))) : null;
        screen ??= main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary;
        if (screen is null) { window.Position = new(main.Position.X + 80, main.Position.Y + 80); return; }
        var area = screen.WorkingArea; var scale = screen.Scaling;
        if (!mini && saved is not null)
        {
            window.Width = Math.Clamp(double.IsFinite(saved.Width) ? saved.Width : 880, 360, Math.Max(360, area.Width / scale));
            window.Height = Math.Clamp(double.IsFinite(saved.Height) ? saved.Height : 190, 130, Math.Max(130, area.Height / scale));
        }
        var width = Math.Min(area.Width, (int)Math.Ceiling(window.Width * scale));
        var height = Math.Min(area.Height, (int)Math.Ceiling(window.Height * scale));
        var x = saved?.X ?? area.X + (area.Width - width) / 2;
        var y = saved?.Y ?? (mini ? area.Y + (int)(28 * scale) : area.Bottom - height - (int)(40 * scale));
        window.Position = new(Math.Clamp(x, area.X, area.Right - width), Math.Clamp(y, area.Y, area.Bottom - height));
    }
    public static FloatingBounds Capture(Window window) => new(window.Position.X, window.Position.Y, window.Width, window.Height);
}
