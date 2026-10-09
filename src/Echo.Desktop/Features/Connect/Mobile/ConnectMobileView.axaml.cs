using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Connect.Mobile;

public partial class ConnectMobileView : UserControl
{
    public ConnectMobileView() => InitializeComponent();

    private async void OnCopyAddress(object? sender, RoutedEventArgs e) => await Copy(model => model.Address, "已复制遥控地址。");
    private async void OnCopyCode(object? sender, RoutedEventArgs e) => await Copy(model => model.Code, "已复制配对码。");

    private async Task Copy(Func<ConnectMobileViewModel, string> value, string done)
    {
        if (DataContext is not ConnectMobileViewModel model) return;
        var text = value(model);
        if (text is "开启后显示" or "未开始") { model.Card.Hint = "开启配对后才能复制。"; return; }
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) { model.Card.Hint = "当前环境不能写入剪贴板。"; return; }
        await clipboard.SetValueAsync(DataFormat.Text, text);
        model.Card.Hint = done;
    }
}
