using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Desktop.Features.LibraryDetails;
using Echo.Desktop.Localization;
using Echo.Desktop.Controls;

internal static class FilterReferenceCapture
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var text = new LocalizationService(); text.SetLanguage("zh-CN");
        var model = new LibraryFilterViewModel(); var panel = new LibraryFilterView { DataContext = model };
        var host = new Canvas { Width = 841 / 1.75, Height = 766 / 1.75 };
        var surface = new Border { Child = panel, CornerRadius = new(4), BorderThickness = new(1) };
        surface.Bind(Border.BackgroundProperty, surface.GetResourceObservable("EchoField"));
        surface.Bind(Border.BorderBrushProperty, surface.GetResourceObservable("EchoDivider"));
        Canvas.SetLeft(surface, 188 / 1.75); Canvas.SetTop(surface, 96 / 1.75); host.Children.Add(surface);
        var tools = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 19, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        var moreGlyph = new GlyphIcon { Kind = "more", Width = 18, Height = 18 };
        moreGlyph.Bind(GlyphIcon.StrokeProperty, moreGlyph.GetResourceObservable("EchoText"));
        var more = new Button { Width = 32, Height = 34, Content = moreGlyph };
        more.Classes.Add("icon"); tools.Children.Add(more);
        var filter = new Button { Width = 98, Height = 34 };
        filter.Classes.Add("songTool");
        var filterContent = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        filterContent.Children.Add(new GlyphIcon { Kind = "filter", Width = 18, Height = 18 });
        filterContent.Children.Add(new TextBlock { Text = text["query.filters"], VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        var chevron = new GlyphIcon { Kind = "chevron-down", Width = 12, Height = 12 };
        chevron.Bind(GlyphIcon.StrokeProperty, chevron.GetResourceObservable("EchoMuted")); filterContent.Children.Add(chevron);
        filter.Content = filterContent; tools.Children.Add(filter);
        Canvas.SetLeft(tools, 569 / 1.75); Canvas.SetTop(tools, 21 / 1.75); host.Children.Add(tools);
        var window = new Window { Width = 841, Height = 766, Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(1.75,1.75), Child = host } };
        foreach (var key in text.Catalog.Fallback.Strings.Keys) window.Resources[key] = text[key];
        window.Background = SolidColorBrush.Parse("#f5f5f8");
        try
        {
            window.Show(); Pump(); Capture("filter-reference-light.png");
            var condition = panel.FindControl<ComboBox>("ConditionChoice")!;
            condition.SelectedIndex = 3; Pump(); Check(model.FavoriteOnly && model.LosslessOnly, "combined conditions");
            condition.SelectedIndex = 0; Pump(); Check(!model.HasActive, "clear conditions");
            var year = panel.FindControl<ComboBox>("YearChoice")!;
            year.SelectedItem = year.ItemsSource!.Cast<FilterChoice>().First(choice => choice.Value == "2026");
            Pump(); Check(model.Year == "2026", "year dropdown"); model.Reset(); Pump();
            var rate = panel.FindControl<ComboBox>("RateChoice")!; rate.SelectedIndex = 4;
            model.DepthIndex = 2; Pump(); Check(model.Snapshot().BitDepth == 24 && model.HasActive, "advanced values retained");
            model.Reset(); Pump();
            panel.FindControl<Expander>("MoreFilters")!.IsExpanded = true; Pump(); Capture("filter-advanced-light.png");
            panel.FindControl<Expander>("MoreFilters")!.IsExpanded = false;
            window.RequestedThemeVariant = ThemeVariant.Dark; window.Background = SolidColorBrush.Parse("#14151a"); Pump(); Capture("filter-reference-dark.png");
            Console.WriteLine("Native reference panel: condition/year/rate/reset, advanced controls, 175% light/dark rendering passed.");
        }
        finally { window.Close(); }
        void Capture(string name)
        {
            Pump(); using var bitmap = new RenderTargetBitmap(new PixelSize(841,766), new Vector(96,96));
            bitmap.Render(window); bitmap.Save(Path.Combine(output,name), PngBitmapEncoderOptions.Default);
        }
    }
    private static void Check(bool result, string label) { if (!result) throw new InvalidOperationException(label); }
    private static void Pump() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
}
