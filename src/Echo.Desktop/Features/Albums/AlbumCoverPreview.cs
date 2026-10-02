using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Albums;

public partial class AlbumDetailView
{
    private IInputElement? _coverReturnFocus;
    private void OnPreviewCover(object? sender, RoutedEventArgs args)
    {
        if (_closed || _lease?.Image is null) return;
        _coverReturnFocus = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        // The existing bounded cover lease is shared; opening the overlay allocates no second bitmap.
        PreviewImage.Source = _lease.Image; CoverPreview.IsVisible = true;
        KeyboardNavigation.SetTabNavigation(CoverPreview, KeyboardNavigationMode.Cycle);
        PreviewClose.Focus(); args.Handled = true;
    }
    private void OnPreviewScrim(object? sender, PointerPressedEventArgs args)
    {
        if (ReferenceEquals(args.Source, CoverPreview)) ClosePreview();
        args.Handled = true;
    }
    private void OnPreviewKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Escape) { ClosePreview(); args.Handled = true; } }
    private void OnClosePreview(object? sender, RoutedEventArgs args) { ClosePreview(); args.Handled = true; }
    private void ClosePreview()
    {
        if (!CoverPreview.IsVisible) return;
        CoverPreview.IsVisible = false; PreviewImage.Source = null;
        if (!_closed) _coverReturnFocus?.Focus();
        _coverReturnFocus = null;
    }
}
