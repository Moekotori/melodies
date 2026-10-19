using Avalonia.Controls;

namespace Echo.Desktop.Features.Settings.Drawers;

public partial class QuickSettingsDrawer
{
    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (SearchFeedback is null) return;
        var query = DrawerSearchInput.Text ?? "";
        var found = (DrawerContent.Content as IDrawerSearchTarget)?.FindSetting(query) != false;
        SearchFeedback.IsVisible = !found;
        SearchFeedback.Text = !found ? _model?.Text["ui.SettingsView.f80e5af47264"] : "";
    }
}
