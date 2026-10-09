using Avalonia.Controls;

namespace Echo.Desktop.Features.Settings.Controls;

public partial class SettingsSectionView : UserControl
{
    public SettingsSectionView() => InitializeComponent();
    public SettingsSectionView(SettingsViewModel model, bool showHeading = true) : this()
    {
        DataContext = model;
        CategoryHeading.IsVisible = showHeading && !model.IsAppearance;
        if (model.IsAppearance)
        {
            ThemeContent.Content = new ThemeSettingsView { DataContext = model.Shell.Theme };
            ExtraContent.Content = new MotionSettingsView { DataContext = model.Shell, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        }
        if (model.IsPlayback)
            ExtraContent.Content = new PlaybackSettingsView { DataContext = model.Shell };
        if (model.ActiveCategory.Id == "general" && model.UserLanguages is { } languages)
            UserLanguagesContent.Content = new UserLanguagesView { DataContext = languages };
    }
}
