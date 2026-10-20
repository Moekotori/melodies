using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Songs;

// Search lives behind a toolbar icon. It expands in place and folds back once focus leaves with an empty query.
public partial class SongsView
{
    private void InitSearchChrome()
    {
        if (Model is { Search.Length: > 0 }) SetSearchOpen(true);
        SearchShell.LostFocus += (_, _) => Dispatcher.UIThread.Post(CollapseSearchIfIdle, DispatcherPriority.Input);
    }
    public void FocusSearch() { SetSearchOpen(true); SearchField.Focus(); SearchField.SelectAll(); }
    private void SetSearchOpen(bool open) { SearchShell.IsVisible = open; SearchButton.IsVisible = !open; }
    private void OnSearchOpen(object? sender, RoutedEventArgs args) { FocusSearch(); args.Handled = true; }
    private void CollapseSearchIfIdle()
    {
        if (closed || Model is null || Model.Search.Length > 0 || CommandButton.Flyout?.IsOpen == true) return;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Avalonia.Visual;
        if (focused is not null && focused.GetSelfAndVisualAncestors().Contains(SearchShell)) return;
        SetSearchOpen(false);
    }
    private void OnSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { _ = Model?.LoadAsync(); args.Handled = true; }
        if (args.Key != Key.Escape || Model is null) return;
        if (Model.Search.Length > 0) Model.Search = ""; else { SetSearchOpen(false); TrackList.Focus(); }
        args.Handled = true;
    }
}
