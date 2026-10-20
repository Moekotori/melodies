using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Echo.Desktop.Shell;

public partial class TitleBar
{
    private TitlebarTextDialog? _textDialog;

    private void OnBrandPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Host is not { } host) return;
        if (e.ClickCount == 2) OnMaximize(sender, e);
        else host.BeginMoveDrag(e);
        e.Handled = true;
    }

    private async void OnEditTitleText(object? sender, RoutedEventArgs e)
    {
        if (_textDialog is not null || Host is not { } host || DataContext is not ShellViewModel shell) return;
        BrandText.ContextMenu?.Close();
        var dialog = _textDialog = new TitlebarTextDialog(shell.Text, shell.TitlebarText, host)
        { RequestedThemeVariant = ActualThemeVariant };
        try
        {
            var value = await dialog.ShowDialog<string?>(host);
            if (value is not null) shell.TitlebarText = value;
        }
        finally { _textDialog = null; }
    }

    private void OnResetTitleText(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell) shell.TitlebarText = "";
    }

    private void OnSelectDirectClose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell) shell.CloseToTray = false;
    }

    private void OnSelectBackgroundClose(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShellViewModel shell) shell.CloseToTray = true;
    }
}
