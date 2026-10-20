using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Radio;

public partial class RadioDirectoryPanel : UserControl
{
    public RadioDirectoryPanel()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ArrangeFilters();
        KeyDown += OnKey;
    }

    private void ArrangeFilters()
    {
        var compact = Bounds.Width < 720;
        Filters.RowDefinitions = new(compact ? "Auto,Auto" : "Auto");
        Filters.ColumnDefinitions = new(compact ? "*,*,*" : "2*,*,*,*,Auto");
        SearchButton.HorizontalAlignment = compact ? Avalonia.Layout.HorizontalAlignment.Right : Avalonia.Layout.HorizontalAlignment.Stretch;
        Grid.SetColumnSpan(NameFilter, compact ? 2 : 1);
        Grid.SetColumn(CountryFilter, compact ? 0 : 1); Grid.SetRow(CountryFilter, compact ? 1 : 0);
        Grid.SetColumn(LanguageFilter, compact ? 1 : 2); Grid.SetRow(LanguageFilter, compact ? 1 : 0);
        Grid.SetColumn(TagFilter, compact ? 2 : 3); Grid.SetRow(TagFilter, compact ? 1 : 0);
        Grid.SetColumn(SearchButton, compact ? 2 : 4);
    }

    private void OnKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter && args.Source is TextBox && DataContext is RadioViewModel model)
        {
            model.Directory.SearchCommand.Execute(null);
            args.Handled = true;
        }
    }

    public void FocusSearch() => NameFilter.Focus();

    private async void OnPlay(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: RadioDirectoryRow row } && DataContext is RadioViewModel model)
            await model.Directory.PlayAsync(row);
    }

    private async void OnAdd(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: RadioDirectoryRow row }) return;
        if (this.GetVisualAncestors().OfType<RadioView>().FirstOrDefault() is not { } owner) return;
        try { owner.Model.Directory.LimitMessage = ""; await owner.Model.Directory.AddAsync(row); }
        catch (Exception error) { owner.Model.Directory.LimitMessage = error.Message; }
    }
}
