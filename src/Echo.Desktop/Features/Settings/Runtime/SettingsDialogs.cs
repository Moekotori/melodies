using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed record SettingsField(string Key, string Label, string Value = "", bool Password = false, string[]? Choices = null);

// User-triggered forms. Values remain local; consumers validate before applying them.
public sealed class SettingsDialogs(Window owner)
{
    public async Task<Dictionary<string, string>> EditAsync(string title, string description, params SettingsField[] fields)
    {
        var dialog = new Window { Title = title, Width = 520, SizeToContent = SizeToContent.Height, MaxHeight = 760,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var readers = new Dictionary<string, Func<string>>(StringComparer.Ordinal);
        var content = new StackPanel { Spacing = 14, Margin = new(24) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeight.SemiBold });
        if (description.Length > 0) content.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap });
        foreach (var field in fields)
        {
            content.Children.Add(new TextBlock { Text = field.Label, FontSize = 13 });
            if (field.Choices is { } choices)
            {
                var select = new ComboBox { ItemsSource = choices, SelectedItem = choices.Contains(field.Value) ? field.Value : choices[0], HorizontalAlignment = HorizontalAlignment.Stretch };
                content.Children.Add(select); readers[field.Key] = () => select.SelectedItem as string ?? "";
            }
            else
            {
                var input = new TextBox { Text = field.Value, PasswordChar = field.Password ? '●' : default, MaxLength = field.Password ? 4096 : 2048 };
                content.Children.Add(input); readers[field.Key] = () => input.Text?.Trim() ?? "";
            }
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new(0, 10, 0, 0) };
        var cancel = new Button { Content = "取消", IsCancel = true }; cancel.Click += (_, _) => dialog.Close(null);
        var apply = new Button { Content = "应用", IsDefault = true }; apply.Classes.Add("primary");
        apply.Click += (_, _) => dialog.Close(readers.ToDictionary(pair => pair.Key, pair => pair.Value(), StringComparer.Ordinal));
        buttons.Children.Add(cancel); buttons.Children.Add(apply); content.Children.Add(buttons);
        dialog.Content = new ScrollViewer { Content = content };
        var values = await dialog.ShowDialog<Dictionary<string, string>?>(owner);
        return values ?? throw new OperationCanceledException("已取消，设置保持原值。");
    }
    public async Task ConfirmAsync(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false };
        var body = new StackPanel { Margin = new(24), Spacing = 16 };
        body.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", IsCancel = true }; cancel.Click += (_, _) => dialog.Close(false);
        var confirm = new Button { Content = "确认" }; confirm.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancel); buttons.Children.Add(confirm); body.Children.Add(buttons); dialog.Content = body;
        if (!await dialog.ShowDialog<bool>(owner)) throw new OperationCanceledException("已取消。");
    }
    public async Task ShowAsync(string title, string message)
    {
        var dialog = new Window { Title = title, Width = 580, MaxHeight = 680, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Margin = new(24), Spacing = 14 };
        body.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeight.SemiBold });
        body.Children.Add(new TextBox { Text = message, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 500 });
        var close = new Button { Content = "关闭", HorizontalAlignment = HorizontalAlignment.Right }; close.Click += (_, _) => dialog.Close(); body.Children.Add(close);
        dialog.Content = body; await dialog.ShowDialog(owner);
    }
}
