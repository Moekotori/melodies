using Avalonia.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Avalonia.Interactivity;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsView
{
    private void InitializeBackgroundInput()
    {
        BackgroundScene.FallbackChanged+=Backdrop.SetBackgroundFallback;
        shell.PropertyChanged+=OnBackgroundHostChanged;SetBackgroundTheme();
        PointerExited+=OnBackgroundPointerExited;
        AddHandler(PointerMovedEvent,OnBackgroundPointerMoved,RoutingStrategies.Tunnel,true);
        AddHandler(PointerPressedEvent,OnBackgroundPointerPressed,RoutingStrategies.Tunnel,true);
    }
    private void SetBackgroundTheme(){BackgroundScene.SetDark(shell.IsDark);Backdrop.SetBackgroundTheme(shell.IsDark);}
    private void OnBackgroundHostChanged(object? sender,System.ComponentModel.PropertyChangedEventArgs e)
    {if(e.PropertyName==nameof(Shell.ShellViewModel.IsDark))SetBackgroundTheme();}
    private void OnBackgroundPointerMoved(object? sender,PointerEventArgs e)=>BackgroundScene.Pointer(e.GetPosition(BackgroundScene),false);
    private void OnBackgroundPointerPressed(object? sender,PointerPressedEventArgs e)
    {
        if(e.Source is Visual target&&target.GetVisualAncestors().Prepend(target).Any(v=>v is Button or TextBox or RangeBase or SelectingItemsControl or MenuItem))return;
        if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)BackgroundScene.Pointer(e.GetPosition(BackgroundScene),true);
    }
    private void OnBackgroundPointerExited(object? sender,PointerEventArgs e)=>BackgroundScene.PointerLeave();
    private void DisposeBackgroundInput()
    {
        BackgroundScene.FallbackChanged-=Backdrop.SetBackgroundFallback;shell.PropertyChanged-=OnBackgroundHostChanged;
        PointerExited-=OnBackgroundPointerExited;
        RemoveHandler(PointerMovedEvent,OnBackgroundPointerMoved);RemoveHandler(PointerPressedEvent,OnBackgroundPointerPressed);
    }
}
