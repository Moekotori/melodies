using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Controls;

// Source-linked product renderer and resources. No desktop services or library I/O.
AppBuilder.Configure<IconPreviewApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
var output = Path.GetFullPath(args.Length > 0 ? args[0] : "misc/icon-redesign-v3/preview");
Directory.CreateDirectory(output);
(string Id, string Label)[] navigation =
[
    ("home", "主页"), ("songs", "曲目"), ("artists", "艺术家"), ("folders", "文件夹"), ("albums", "专辑"), ("genres", "流派"),
    ("remote", "网盘 / 远程"), ("radio", "电台"), ("dsp", "音效处理"), ("workshop", "创意工坊"), ("queue", "队列"), ("history", "历史"),
    ("playlists", "收藏与歌单"), ("listening", "一起听歌"), ("settings", "设置"), ("plugins", "插件"), ("liked", "喜欢"), ("connect", "连接")
];
(string Id, string Label)[] tools = [("grip", "拖动"), ("eye", "显示"), ("eye-off", "隐藏"), ("close", "退出"), ("collapse", "收起"), ("chevron-right", "展开")];
var sidebarStudies = new List<Control>();
foreach (var dark in new[] { false, true })
{
    var background = Brush(dark ? "#15161C" : "#F5F5F9");
    var muted = Brush(dark ? "#B5B8C8" : "#656A7D");
    var text = Brush(dark ? "#EAEBF4" : "#252837");
    var accent = Brush(dark ? "#B2A5FF" : "#6250D6");
    var heading = new StackPanel { Spacing = 8 };
    heading.Children.Add(Label("ECHO  /  轻线版图标", 25, text, FontWeight.SemiBold));
    heading.Children.Add(Label("轻线条 · 单色 · 光学比例", 13, muted));
    var catalog = new Grid { Width = 840, Height = 626, RowDefinitions = RowDefinitions.Parse("78,*,28"), Margin = new Thickness(36) };
    catalog.Children.Add(heading);
    var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*,*,*,*,*"), RowDefinitions = RowDefinitions.Parse("*,*,*,*") };
    Grid.SetRow(grid, 1); catalog.Children.Add(grid);
    var all = navigation.Concat(tools).ToArray();
    for (var i = 0; i < all.Length; i++)
    {
        var (id, title) = all[i];
        var stack = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(Icon(id, i < navigation.Length ? 32 : 24, muted));
        stack.Children.Add(Label(title, 13, muted));
        Grid.SetColumn(stack, i % 6); Grid.SetRow(stack, i / 6); grid.Children.Add(stack);
    }
    var footer = Label("产品 SVG 与原生绘制器渲染  /  24 个图标", 11, muted);
    Grid.SetRow(footer, 2); catalog.Children.Add(footer);
    Save(new Border { Background = background, Child = catalog }, 912, 698, "icons-" + (dark ? "dark" : "light"));

    var rows = new StackPanel { Spacing = 2 };
    var libraryTitle = Label("资料库", 12, text, FontWeight.SemiBold);
    libraryTitle.HorizontalAlignment = HorizontalAlignment.Left;
    rows.Children.Add(new Border { Child = libraryTitle, Padding = new Thickness(8, 12, 0, 16) });
    foreach (var (id, title) in navigation.Take(15))
    {
        if (id is "remote" or "queue")
        {
            var groupTitle = Label(id == "remote" ? "来源" : "播放与创作", 11, muted, FontWeight.SemiBold);
            groupTitle.HorizontalAlignment = HorizontalAlignment.Left;
            rows.Children.Add(new Border { Child = groupTitle, Padding = new Thickness(10, 19, 0, 7) });
        }
        var active = id == "home";
        var row = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("30,*"), ColumnSpacing = 7, Height = 38 };
        row.Children.Add(Icon(id, 23, active ? accent : muted));
        var caption = Label(title, 14, active ? accent : muted, active ? FontWeight.SemiBold : FontWeight.Normal);
        caption.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(caption, 1); row.Children.Add(caption);
        if (id == "settings") rows.Children.Add(new Border { Height = 1, Background = Brush(dark ? "#30313B" : "#DEDFE9"), Margin = new Thickness(0, 10, 0, 10) });
        rows.Children.Add(new Border { Child = row, Padding = new Thickness(7, 0), CornerRadius = new CornerRadius(8), Background = active ? Brush(dark ? "#252335" : "#EDEAF9") : Brushes.Transparent });
    }
    var sidebar = new Border { Background = background, Padding = new Thickness(10, 4), Child = rows };
    Save(sidebar, 220, 754, "sidebar-size-" + (dark ? "dark" : "light"));
    sidebarStudies.Add(sidebar);
}
var paired = new Grid { Width = 456, Height = 754, ColumnDefinitions = ColumnDefinitions.Parse("220,220"), ColumnSpacing = 16 };
for (var i = 0; i < sidebarStudies.Count; i++) { Grid.SetColumn(sidebarStudies[i], i); paired.Children.Add(sidebarStudies[i]); }
Save(new Border { Background = Brush("#E7E8F0"), Padding = new Thickness(12), Child = paired }, 480, 778, "sidebar-pair");
Console.WriteLine("Rendered 24 product SVGs in light/dark and a 23px sidebar size study; no desktop or library services.");

IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
TextBlock Label(string title, double size, IBrush brush, FontWeight? weight = null) => new()
{
    Text = title, FontSize = size, Foreground = brush, FontFamily = new FontFamily("Microsoft YaHei"), FontWeight = weight ?? FontWeight.Normal,
    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
};
EchoIcon Icon(string id, double size, IBrush brush) => new()
{
    Kind = id, Width = size, Height = size, BaseBrush = brush, AccentBrush = brush,
    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
};
void Save(Control content, int width, int height, string name)
{
    var window = new Window { Width = width, Height = height, Content = content };
    window.Show(); Dispatcher.UIThread.RunJobs();
    using var image = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
    image.Render(content); image.Save(Path.Combine(output, name + ".png"), PngBitmapEncoderOptions.Default);
    window.Content = null;
    window.Close();
}

sealed class IconPreviewApp : Application;
