using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using Echo.Desktop.Localization;
using Echo.Desktop.Theming;

namespace Echo.Desktop.Features.Onboarding;

public partial class FirstRunView : UserControl
{
    private FirstRunViewModel? model;
    private WindowLocalizationBinding? localization;
    public FirstRunView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 920;
            Journey.IsVisible = !compact;
            WizardLayout.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 250);
            Stage.Margin = new Thickness(compact ? 20 : 36, 24);
        };
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= Changed;
            localization?.Dispose(); localization = null;
            model = DataContext as FirstRunViewModel;
            if (model is not null) { model.PropertyChanged += Changed; localization = new(this, model.Text); UpdatePreview(); }
        };
        DetachedFromVisualTree += (_, _) => { if (model is not null) model.PropertyChanged -= Changed; model = null; localization?.Dispose(); localization = null; };
    }
    private void Changed(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FirstRunViewModel.Step)) { StepScroll.Offset = default; Heading.Focus(); }
        if (args.PropertyName == nameof(FirstRunViewModel.SelectedTheme))
            UpdatePreview();
    }
    private void UpdatePreview()
    {
        if (model?.SelectedTheme is not { } selected) return;
        var dark = selected.Id == "深色" || selected.Id == "跟随系统" && ActualThemeVariant == ThemeVariant.Dark;
        ThemePreview.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemePreview.Resources = ThemeResourcePalette.Create(model.PreviewTone(dark), model.PreviewPreferences, dark, null, wallpaperAvailable: false);
    }
    public void FocusStart() => Heading.Focus();
}
