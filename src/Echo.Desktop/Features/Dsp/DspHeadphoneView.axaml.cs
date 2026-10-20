using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using System.ComponentModel;
namespace Echo.Desktop.Features.Dsp;
public partial class DspHeadphoneView : UserControl
{
    private DspViewModel? observedModel;
    private bool preampRefreshQueued;
    public DspHeadphoneView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ObserveModel(DataContext as DspViewModel);
        AttachedToVisualTree += (_, _) => ObserveModel(DataContext as DspViewModel);
        DetachedFromVisualTree += (_, _) => ObserveModel(null);
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 850;
            HeadphoneLayout.ColumnDefinitions = new(compact ? "*" : "300,*");
            HeadphoneLayout.RowDefinitions = new(compact ? "Auto,Auto" : "Auto");
            Grid.SetColumn(HeadphoneDetails, compact ? 0 : 1); Grid.SetRow(HeadphoneDetails, compact ? 1 : 0);
        };
    }
    private void ObserveModel(DspViewModel? model)
    {
        if (observedModel is not null) observedModel.PropertyChanged -= RefreshPreamp;
        observedModel = model;
        if (model is not null) model.PropertyChanged += RefreshPreamp;
    }
    private void RefreshPreamp(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DspViewModel.HeadphonePreampValue) || preampRefreshQueued) return;
        // A rejected two-way write keeps the source unchanged; publish after the binding write unwinds.
        preampRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            preampRefreshQueued = false;
            if (observedModel is not { } model) return;
            HeadphonePreamp.SetCurrentValue(NumericUpDown.ValueProperty, model.HeadphonePreampValue);
            HeadphonePreampSlider.SetCurrentValue(RangeBase.ValueProperty, model.PreampDb);
        });
    }
    private async void ImportCurve(object? sender, RoutedEventArgs e) => await DspPresetDialogs.ImportAsync(this);
    private async void ExportCurve(object? sender, RoutedEventArgs e) => await DspPresetDialogs.ExportAsync(this);
    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not DspViewModel { CanUseResources: true } model) return;
        model.SearchHeadphonesCommand.Execute(null); e.Handled = true;
    }
}
