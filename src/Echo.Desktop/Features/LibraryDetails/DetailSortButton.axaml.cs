using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.LibraryDetails;

public partial class DetailSortButton : UserControl
{
    public static readonly StyledProperty<int> SelectedIndexProperty = AvaloniaProperty.Register<DetailSortButton, int>(nameof(SelectedIndex), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> DescendingProperty = AvaloniaProperty.Register<DetailSortButton, bool>(nameof(Descending), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> IsInlineProperty = AvaloniaProperty.Register<DetailSortButton, bool>(nameof(IsInline));
    public bool IsInline { get => GetValue(IsInlineProperty); set => SetValue(IsInlineProperty, value); }
    public int SelectedIndex { get => GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public bool Descending { get => GetValue(DescendingProperty); set => SetValue(DescendingProperty, value); }
    private IDisposable? _labelBinding;
    public DetailSortButton()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => UpdateState();
        DetachedFromVisualTree += (_, _) => { SortOpener.Flyout?.Hide(); _labelBinding?.Dispose(); _labelBinding = null; };
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == IsInlineProperty && InlineHost is not null)
        {
            var flyout = (Flyout)SortOpener.Flyout!;
            flyout.Hide();
            if (IsInline) { flyout.Content = null; InlineHost.Child = SortOptions; }
            else { InlineHost.Child = null; flyout.Content = SortOptions; }
            InlineHost.IsVisible = IsInline; SortOpener.IsVisible = !IsInline;
        }
        if ((args.Property == SelectedIndexProperty || args.Property == DescendingProperty) && SelectedLabel is not null) UpdateState();
    }
    private void UpdateState()
    {
        var key = SelectedIndex switch { 1 => "ui.AlbumDetailView.d44e9b3d3b31", 2 => "ui.AlbumDetailView.04a95e0654d3", 3 => "ui.AlbumDetailView.eb11dd93ee99", _ => "detail.sort.order" };
        _labelBinding?.Dispose(); _labelBinding = SelectedLabel.Bind(TextBlock.TextProperty, this.GetResourceObservable(key));
        DirectionLabel.Text = Descending ? "↓" : "↑";
        DirectionLabel.IsVisible = SelectedIndex != 0 || Descending;
        OrderCheck.IsVisible = SelectedIndex == 0; TitleCheck.IsVisible = SelectedIndex == 1;
        DurationCheck.IsVisible = SelectedIndex == 2; QualityCheck.IsVisible = SelectedIndex == 3;
        AscendingCheck.IsVisible = !Descending; DescendingCheck.IsVisible = Descending;
    }
    private void OnSort(object? sender, RoutedEventArgs args)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var index)) SetCurrentValue(SelectedIndexProperty, index);
        SortOpener.Flyout?.Hide(); args.Handled = true;
    }
    private void OnDirection(object? sender, RoutedEventArgs args)
    {
        if (sender is Button button && bool.TryParse(button.Tag?.ToString(), out var reverse)) SetCurrentValue(DescendingProperty, reverse);
        SortOpener.Flyout?.Hide(); args.Handled = true;
    }
}
