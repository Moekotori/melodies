using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Connect.Receive;

public partial class ConnectReceiveView : UserControl
{
    public ConnectReceiveView() => InitializeComponent();

    private async void OnCopyAddress(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectReceiveViewModel model || model.ListenAddress.Length == 0) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            model.Renderer.Hint = "当前环境不能写入剪贴板。";
            return;
        }
        await clipboard.SetValueAsync(DataFormat.Text, model.ListenAddress);
        model.Renderer.Hint = "已复制描述地址。";
    }
}
