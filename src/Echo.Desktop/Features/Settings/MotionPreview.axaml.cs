using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings;

public partial class MotionPreview : UserControl
{
    private string _example = "page";
    private bool _alternate;
    public MotionPreview()
    {
        InitializeComponent();
        UpdateExample();
    }
    private void SelectExample(object? sender, RoutedEventArgs args)
    {
        _example = (sender as Button)?.Tag as string ?? "page";
        _alternate = !_alternate;
        PageTab.Classes.Set("active", _example == "page");
        CoverTab.Classes.Set("active", _example == "cover");
        ListTab.Classes.Set("active", _example == "list");
        UpdateExample();
    }
    private void Replay(object? sender, RoutedEventArgs args) { _alternate = !_alternate; UpdateExample(); }
    private void ToggleDetails(object? sender, RoutedEventArgs args) => Details.IsOpen = !Details.IsOpen;
    private void ShowNotice(object? sender, RoutedEventArgs args)
    { if (DataContext is ShellViewModel model) model.Notice = "动效预览：提示消息已显示，可以点击关闭。"; }
    private void UpdateExample()
    {
        if (_example == "list")
        {
            ExampleHost.Content = new ListBox
            {
                ItemsSource = new[] { "清晨 · 轻快的节奏", "午后 · 柔和的旋律", "夜晚 · 安静的余韵" },
                SelectedIndex = _alternate ? 1 : 0, Height = 140
            };
            return;
        }
        var text = new StackPanel { Spacing = 8, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var title = new TextBlock { Text = _example == "cover" ? _alternate ? "午后旋律" : "夜间漫步" : _alternate ? "最近活动" : "你的音乐", FontSize = 25 };
        title.Classes.Add("heading");
        text.Children.Add(title);
        var caption = new TextBlock { Text = "演示内容 · 不影响当前播放", FontSize = 13 };
        caption.Classes.Add("muted");
        text.Children.Add(caption);
        if (_example == "cover")
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), ColumnSpacing = 24 };
            grid.Children.Add(new Border
            {
                Width = 120, Height = 120, Background = new SolidColorBrush(Color.Parse(_alternate ? "#4b55e8" : "#364958")),
                Child = new TextBlock { Text = _alternate ? "PM" : "AM", FontSize = 32, Foreground = Brushes.White,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }
            });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            ExampleHost.Content = grid;
        }
        else ExampleHost.Content = text;
    }
}
