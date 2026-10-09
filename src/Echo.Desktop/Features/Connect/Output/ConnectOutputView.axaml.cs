using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Connect.Output;

public partial class ConnectOutputView : UserControl
{
    private bool volumeDrag;

    public ConnectOutputView() => InitializeComponent();

    private async void OnCopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectOutputViewModel model) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { model.Message = "当前环境不能写入剪贴板。"; return; }
        await clipboard.SetValueAsync(DataFormat.Text, model.DiagnosticsText);
        model.Message = "已复制输出诊断。";
    }

    private void OnVolumePressed(object? sender, PointerPressedEventArgs e)
    {
        volumeDrag = true;
        if (DataContext is ConnectOutputViewModel { Player: { } player }) player.IsChangingVolume = true;
    }

    private async void OnVolumeCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!volumeDrag) return;
        volumeDrag = false;
        await CommitVolumeAsync(sender as Slider);
    }

    private async void OnVolumeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (volumeDrag) return;
        await CommitVolumeAsync(sender as Slider);
    }

    private async Task CommitVolumeAsync(Slider? slider)
    {
        if (slider is null || DataContext is not ConnectOutputViewModel model) return;
        if (!model.ComputerVolume) { await model.CommitCastVolumeAsync(slider.Value); return; }
        if (model.Player is not { } player) return;
        if (!volumeDrag && Math.Abs(slider.Value - model.Now.VolumePercent) < 0.5) { player.IsChangingVolume = false; return; }
        try { await player.ChangeVolumeAsync(slider.Value / 100d); }
        finally { player.IsChangingVolume = false; }
    }
}
