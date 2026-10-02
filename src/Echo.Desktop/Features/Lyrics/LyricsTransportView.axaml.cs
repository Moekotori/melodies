using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Echo.Desktop.Features.Player;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsTransportView : UserControl
{
    private PlayerViewModel? Player => DataContext as PlayerViewModel;
    public LyricsTransportView()
    {
        InitializeComponent();
        SeekSlider.AddHandler(PointerPressedEvent, (_, _) => { if (Player is { } p) p.IsSeeking = true; }, RoutingStrategies.Tunnel, true);
        SeekSlider.AddHandler(PointerReleasedEvent, async (_, _) =>
        {
            if (Player is not { } p) return;
            try { await p.SeekToAsync(SeekSlider.Value); } finally { p.IsSeeking = false; }
        }, RoutingStrategies.Bubble, true);
        SeekSlider.KeyUp += async (_, _) => { if (Player is { } p) await p.SeekToAsync(SeekSlider.Value); };
        DetachedFromVisualTree += (_, _) => { if (Player is { } p) p.IsSeeking = false; };
    }
    public void Fit(double width) => TransportLayout.Width = Math.Clamp(width - 80, 280, 720);
}
