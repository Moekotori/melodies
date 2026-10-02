using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Echo.Desktop.Controls;
using Echo.Desktop.Shell;

// Render the product SVGs through the same native control and theme as the sidebar.
internal static class IconCatalogCapture
{
    private static readonly (string Id, string Label)[] Items =
    [
        ("home", "主页"), ("songs", "曲目"), ("artists", "艺术家"), ("folders", "文件夹"), ("albums", "专辑"), ("genres", "流派"),
        ("remote", "网盘 / 远程"), ("radio", "电台"), ("dsp", "音效处理"), ("workshop", "创意工坊"), ("queue", "队列"), ("history", "历史"),
        ("playlists", "收藏与歌单"), ("listening", "一起听歌"), ("settings", "设置"), ("plugins", "插件"), ("liked", "喜欢"), ("connect", "连接"),
        ("grip", "拖动"), ("eye", "显示"), ("eye-off", "隐藏"), ("close", "退出"), ("collapse", "收起"), ("chevron-right", "展开")
    ];

    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var model = new ShellViewModel();
        model.Motion.SelectedChoice = model.Motion.Choices.First(x => x.Mode == Echo.Desktop.Motion.MotionMode.Off);
        var window = new MainWindow { DataContext = model, Width = 920, Height = 780 };
        var sheet = new Grid
        {
            Width = 840, Height = 650, Margin = new Thickness(36),
            RowDefinitions = RowDefinitions.Parse("84,*,36")
        };
        var surface = new Border { Child = sheet };
        surface.Bind(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("EchoPage"));
        var header = new StackPanel { Spacing = 7 };
        header.Children.Add(new TextBlock { Text = "ECHO  /  侧栏图标 · 轻线版", FontSize = 25, FontWeight = FontWeight.SemiBold });
        header.Children.Add(new TextBlock { Text = "单色 · 轻线条 · 光学比例", FontSize = 13 });
        sheet.Children.Add(header);
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,*,*,*,*,*"), RowDefinitions = RowDefinitions.Parse("*,*,*,*") };
        Grid.SetRow(grid, 1); sheet.Children.Add(grid);
        for (var i = 0; i < Items.Length; i++)
        {
            var (id, label) = Items[i];
            var stack = new StackPanel { Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var icon = new EchoIcon { Kind = id, Width = i < 18 ? 34 : 26, Height = i < 18 ? 34 : 26, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            icon.Bind(EchoIcon.BaseBrushProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("EchoMuted"));
            stack.Children.Add(icon);
            stack.Children.Add(new TextBlock { Text = label, FontSize = 13, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
            Grid.SetColumn(stack, i % 6); Grid.SetRow(stack, i / 6); grid.Children.Add(stack);
        }
        var footer = new TextBlock { Text = "18 个导航图标  +  6 个编辑控件图标  /  实际 Avalonia 控件渲染", FontSize = 12, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        Grid.SetRow(footer, 2); sheet.Children.Add(footer);
        window.Content = surface; window.Show();
        foreach (var dark in new[] { false, true })
        {
            model.IsDark = dark; Dispatcher.UIThread.RunJobs();
            using var frame = new RenderTargetBitmap(new PixelSize(912, 722), new Vector(96, 96));
            frame.Render(surface);
            frame.Save(Path.Combine(output, dark ? "icons-dark.png" : "icons-light.png"), PngBitmapEncoderOptions.Default);
        }
        window.Close(); model.Settings.Dispose(); model.ReleaseLocalization();
        Console.WriteLine("Native icon catalog: 24 icons, light and dark PNGs.");
    }
}
