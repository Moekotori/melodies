using System.Diagnostics;
using System.Runtime.InteropServices;
using Echo.Desktop.Features.Taskbar;
using Echo.Platform.Windows;

internal static class NativeResourcesSmoke
{
    public static void Run(string output)
    {
        if (!OperatingSystem.IsWindows()) return;
        var window = CreateWindowExW(0, "STATIC", "ECHO taskbar resource smoke", 0, 0, 0, 256, 256, 0, 0, 0, 0);
        if (window == 0) throw new InvalidOperationException("Native test HWND unavailable.");
        var messages = new List<string>();
        try
        {
            using var process = Process.GetCurrentProcess();
            uint beforeGdi = 0, beforeUser = 0;
            // Shell/COM lazily creates process-global resources on its first activation.
            for (var repeat = -1; repeat < 3; repeat++)
            {
                if (repeat == 0) { beforeGdi = GetGuiResources(process.Handle, 0); beforeUser = GetGuiResources(process.Handle, 1); }
                using var player = new WindowsTaskbarPlayer(window);
                player.Failed += message => messages.Add(message);
                player.SetIcons(TaskbarArtworkRenderer.Icons(repeat % 2 == 0));
                var state = new TaskbarPlayerState(true, true, true, true, true, true, false, true, 2, 30, 120,
                    "Smoke", ["Previous", "Pause", "Next", "Favorite", "Queue"]);
                player.Publish(state); player.Initialize();
                player.SetArtwork(TaskbarArtworkRenderer.Cover(null, false, false, "", ""));
                var calls = new List<int>(); player.Command += calls.Add;
                for (var id = 1; id <= 5; id++) player.HandleMessage(0x111, (nint)((0x1800 << 16) | id), 0);
                if (!calls.SequenceEqual(new[] { 1, 2, 3, 4, 5 })) throw new InvalidOperationException("Native command decoding");
                player.Publish(state with { CanFavorite = false });
                player.HandleMessage(0x111, (nint)((0x1800 << 16) | 4), 0);
                if (calls.Count != 5) throw new InvalidOperationException("Disabled favorite callback");
                foreach (var size in new[] { 48, 128, 256, 384 }) player.HandleMessage(0x323, 0, (nint)((size << 16) | size));
                player.HandleMessage(0x326, 0, 0);
                player.Publish(state with { Enabled = false });
                player.HandleMessage(0x111, (nint)((0x1800 << 16) | 2), 0);
                if (calls.Count != 5) throw new InvalidOperationException("Disabled native toolbar");
            }
            var gdiDelta = (int)GetGuiResources(process.Handle, 0) - (int)beforeGdi;
            var userDelta = (int)GetGuiResources(process.Handle, 1) - (int)beforeUser;
            File.WriteAllLines(Path.Combine(output, "native-report.txt"),
                new[] { $"Three lifecycle passes after one warmup; GDI delta: {gdiDelta}; USER delta: {userDelta}",
                    "Native shell errors are availability diagnostics; no Explorer hover acceptance." }.Concat(messages.Distinct()));
            if (gdiDelta > 2 || userDelta > 2) throw new InvalidOperationException("Native handles retained after disposal.");
        }
        finally { DestroyWindow(window); }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetGuiResources(nint process, uint flags);
}
