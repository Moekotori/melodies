using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Echo.Desktop.Features.LibraryDetails;

// The choice list is bounded by its caller; searching never queries or copies the library.
public sealed class FilterChoiceBox : ComboBox
{
    public static readonly AttachedProperty<bool> SearchEnabledProperty = AvaloniaProperty.RegisterAttached<FilterChoiceBox, ComboBox, bool>("SearchEnabled");
    public static bool GetSearchEnabled(ComboBox control) => control.GetValue(SearchEnabledProperty);
    public static void SetSearchEnabled(ComboBox control, bool value) => control.SetValue(SearchEnabledProperty, value);
    private IReadOnlyList<FilterChoice> choices = [];
    private TextBox? search;
    private FilterChoice? selectedChoice;
    private string searchText = "";
    public FilterChoiceBox()
    {
        DropDownOpened += (_, _) =>
        {
            searchText = "";
            if (search is not null) search.Text = "";
            ApplySearch();
        };
        SelectionChanged += (_, _) => { if (SelectedItem is FilterChoice choice) selectedChoice = choice; };
        DropDownClosed += (_, _) => { searchText = ""; ApplySearch(); };
    }
    protected override Type StyleKeyOverride => typeof(ComboBox);
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (search is not null) search.TextChanged -= OnSearch;
        base.OnApplyTemplate(e);
        search = e.NameScope.Find<TextBox>("PART_FilterSearch");
        if (search is not null) search.TextChanged += OnSearch;
    }
    internal void SetChoices(IReadOnlyList<FilterChoice> options, FilterChoice selected)
    {
        choices = options; selectedChoice = selected;
        ItemsSource = VisibleChoices();
        SelectedItem = selected;
    }
    private void OnSearch(object? sender, TextChangedEventArgs e)
    {
        searchText = search?.Text?.Trim() ?? "";
        ApplySearch();
    }
    private IReadOnlyList<FilterChoice> VisibleChoices() => searchText.Length == 0 ? choices
        : choices.Where(choice => choice.Label.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || choice.Value.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)).ToArray();
    private void ApplySearch()
    {
        var selected = selectedChoice;
        ItemsSource = VisibleChoices();
        // A hidden selection stays in the model until the user chooses another item.
        if (selected is FilterChoice choice && ItemsSource!.Cast<FilterChoice>().Contains(choice)) SelectedItem = selected;
    }
}
