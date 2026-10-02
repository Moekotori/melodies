using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Search;

// Window-owned overlay; hidden state retains no request, library DTO or playback action.
public partial class GlobalSearchOverlay : UserControl
{
    private readonly MotionLane motion = new();
    private ShellViewModel? shell;
    private Window? window;
    private IInputElement? returnFocus;
    private int generation;
    public GlobalSearchViewModel? Model { get; private set; }
    public Echo.Desktop.Features.Albums.AlbumArtworkCache? Artwork => shell?.AlbumArtwork;
    public event Action<GlobalSearchHit>? OpenRequested;
    public GlobalSearchOverlay()
    {
        InitializeComponent(); IsVisible = false;
        KeyboardNavigation.SetTabNavigation(SearchPanel, KeyboardNavigationMode.Cycle);
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) { Detach(); Attach(); } };
        SizeChanged += (_, _) => UpdateBounds();
    }
    private void Attach()
    {
        shell = DataContext as ShellViewModel; window = TopLevel.GetTopLevel(this) as Window;
        if (shell is null) return;
        shell.PropertyChanged += OnShellChanged; shell.Motion.PropertyChanged += OnPolicyChanged;
        window?.AddHandler(KeyDownEvent, OnWindowKey, RoutingStrategies.Tunnel);
        if (shell.IsGlobalSearchOpen) Transition();
    }
    private void Detach()
    {
        ++generation; motion.Cancel(); ReleaseContent();
        if (shell is not null) { shell.PropertyChanged -= OnShellChanged; shell.Motion.PropertyChanged -= OnPolicyChanged; }
        window?.RemoveHandler(KeyDownEvent, OnWindowKey);
        shell = null; window = null; returnFocus = null; IsVisible = false;
    }
    private void OnShellChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.IsGlobalSearchOpen)) Transition();
        if (args.PropertyName == nameof(ShellViewModel.NavigationVersion) ||
            args.PropertyName == nameof(ShellViewModel.IsQuickSettingsOpen) && shell?.IsQuickSettingsOpen == true)
            shell?.CloseGlobalSearchCommand.Execute(null);
    }
    private async void Transition()
    {
        if (shell is null) return;
        var opening = shell.IsGlobalSearchOpen; var version = ++generation;
        var opacity = IsVisible ? MotionLane.CurrentOpacity(SearchPanel) : 0;
        var transform = IsVisible ? MotionLane.Current(SearchPanel) : MotionLane.At(y: -10);
        motion.Cancel();
        if (opening)
        {
            if (Model is null)
            {
                returnFocus = window?.FocusManager?.GetFocusedElement();
                Model = new(shell); Model.PropertyChanged += OnResultsChanged; SearchPanel.DataContext = Model;
            }
        }
        else { ReleaseContent(); RestoreFocus(); }
        IsVisible = true; IsHitTestVisible = opening;
        KeyboardNavigation.SetTabNavigation(SearchPanel, opening ? KeyboardNavigationMode.Cycle : KeyboardNavigationMode.None);
        UpdateBounds();
        if (opening)
        {
            // A newly visible overlay needs its first layout before TextBox can accept focus.
            Dispatcher.UIThread.Post(() =>
            {
                if (version != generation || shell?.IsGlobalSearchOpen != true) return;
                SearchPanel.UpdateLayout(); FocusSearch();
            }, DispatcherPriority.Loaded);
        }
        var policy = shell.Motion;
        await motion.PlayAsync(SearchPanel, opacity, opening ? 1 : 0,
            policy.UseSpatialMotion ? transform : MotionLane.At(), MotionLane.At(y: !opening && policy.UseSpatialMotion ? -10 : 0),
            !policy.CanAnimate ? TimeSpan.Zero : policy.UseSpatialMotion ? opening ? MotionTokens.Reveal : MotionTokens.Exit : MotionTokens.Reduced,
            preserveGeometry: true);
        if (version == generation)
        {
            IsVisible = shell?.IsGlobalSearchOpen == true; SearchPanel.Opacity = 1; SearchPanel.RenderTransform = MotionLane.At();
        }
    }
    private void ReleaseContent()
    {
        if (Model is not null) { Model.PropertyChanged -= OnResultsChanged; Model.Dispose(); }
        Model = null; playing?.Dispose(); playing = null;
        SearchPanel.DataContext = null; HitList.SelectedItem = null;
    }
    private void RestoreFocus()
    {
        if (returnFocus is Control { IsEffectivelyVisible: true } previous && previous.IsAttachedToVisualTree()) previous.Focus();
        else window?.FocusManager?.Focus(null);
        returnFocus = null;
    }
    private void UpdateBounds()
    {
        SearchPanel.Width = Bounds.Width > 0 ? Math.Min(760, Math.Max(0, Bounds.Width - 36)) : 760;
        var top = Math.Max(76, Bounds.Height * .12);
        SearchPanel.Margin = new(18, top, 18, 24);
        SearchPanel.MaxHeight = Math.Max(74, Bounds.Height - top - 24);
    }
    private void OnPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MotionPolicy.CanAnimate)) return;
        if (shell?.Motion.IsWindowVisible == false) shell.CloseGlobalSearchCommand.Execute(null);
        ++generation; motion.Cancel(); SearchPanel.Opacity = 1; SearchPanel.RenderTransform = MotionLane.At();
        IsVisible = IsHitTestVisible = shell?.IsGlobalSearchOpen == true;
    }
    public void FocusSearch() { SearchField.Focus(); SearchField.SelectionStart = 0; SearchField.SelectionEnd = SearchField.Text?.Length ?? 0; }
    private void OnScrimPressed(object? sender, PointerPressedEventArgs args)
    { shell?.CloseGlobalSearchCommand.Execute(null); args.Handled = true; }
    private void OnClear(object? sender, RoutedEventArgs args) { if (Model is not null) Model.Search = ""; FocusSearch(); }
    private void OnRetry(object? sender, RoutedEventArgs args) { if (Model is not null) _ = Model.LoadAsync(); }
}
