using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Echo.Desktop.Features.Settings.Models;

namespace Echo.Desktop.Features.Settings.Controls;

public partial class SettingRowView : UserControl
{
    private SettingRowViewModel? _model;
    private IDisposable? _highlightBrush;
    public SettingRowView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            _model = DataContext as SettingRowViewModel;
            if (_model is not null)
            {
                RowEditor.ContentTemplate = (IDataTemplate)Resources[_model.Definition.Kind + "Editor"]!;
                RowEditor.Content = _model;
            }
            if (_model is not null) _model.PropertyChanged += OnRowChanged;
            RevealMatch();
        };
        DetachedFromVisualTree += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnRowChanged; _highlightBrush?.Dispose(); _highlightBrush = null; _model = null; };
    }
    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(SettingRowViewModel.IsHighlighted)) RevealMatch(); }
    private void RevealMatch()
    {
        if (_model?.IsHighlighted == true)
        {
            RowBorder.BorderThickness = new Avalonia.Thickness(1);
            _highlightBrush?.Dispose();
            _highlightBrush = RowBorder.Bind(Border.BorderBrushProperty, this.GetResourceObservable("EchoAccent"));
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_model?.IsHighlighted == true) this.BringIntoView(); }, Avalonia.Threading.DispatcherPriority.Loaded);
        }
    }
}
