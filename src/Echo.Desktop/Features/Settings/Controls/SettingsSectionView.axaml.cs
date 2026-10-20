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
            FontContent.Content = new ThemeTypographySettings { DataContext = model.Shell.Theme };
            ExtraContent.Content = new MotionSettingsView { DataContext = model.Shell, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
        }
        if (model.IsPlayback)
            ExtraContent.Content = new PlaybackSettingsView { DataContext = model.Shell };
        if (model.ActiveCategory.Id == "maintenance" && model.Shell.ArtistLibrary is Echo.Contracts.Library.IArtistCreditLibrary library
            && library is Echo.Core.Contracts.ICoreTaskControl tasks)
        {
            var editor = new Echo.Desktop.Features.Artists.ArtistImportView(new(library,tasks));
            ExtraContent.Content = editor;
            DetachedFromVisualTree += (_,_) => editor.Dispose();
        }
        if (model.ActiveCategory.Id == "general" && model.UserLanguages is { } languages)
            UserLanguagesContent.Content = new UserLanguagesView { DataContext = languages };
    }
}
