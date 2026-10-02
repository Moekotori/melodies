using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Echo.Desktop.Motion;

internal static class CompositionSmoke
{
    public static void Run(string output)
    {
        var tile = new Border { Width = 40, Height = 40, Background = Brushes.Red };
        var window = new Window
        {
            Width = 128, Height = 128, WindowDecorations = WindowDecorations.None,
            Background = Brushes.Black, Content = tile
        };
        using var lane = new MotionLane();
        window.Show();
        Pump(50);
        _ = lane.PlayAsync(tile, 0, 1, MotionLane.At(24), MotionLane.At(), TimeSpan.FromMilliseconds(220));
        Pump(55);
        var early = Read(window, Path.Combine(output, "compositor-intermediate.png"));
        Pump(320);
        var final = Read(window, Path.Combine(output, "compositor-final.png"));
        window.Close();
        if (!(early.Red is > 0 and < 250 && final.Red > 250 && early.Top > final.Top))
            throw new InvalidOperationException($"Compositor pixels did not animate: early={early}, final={final}.");
        Console.WriteLine($"Compositor pixel proof: intermediate red={early.Red}, top={early.Top}; final red={final.Red}, top={final.Top}.");
    }
    private static (int Red, int Top) Read(Window window, string path)
    {
        using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No compositor frame.");
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        using var frame = bitmap.Lock();
        var bytes = new byte[frame.RowBytes * frame.Size.Height];
        Marshal.Copy(frame.Address, bytes, 0, bytes.Length);
        var redOffset = frame.Format == PixelFormat.Bgra8888 ? 2 : 0;
        var red = 0;
        var top = frame.Size.Height;
        for (var y = 0; y < frame.Size.Height; y++)
            for (var x = 0; x < frame.Size.Width; x++)
            {
                var value = bytes[y * frame.RowBytes + x * 4 + redOffset];
                if (value > 4) { red = Math.Max(red, value); top = Math.Min(top, y); }
            }
        return (red, top);
    }
    private static void Pump(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(8);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
