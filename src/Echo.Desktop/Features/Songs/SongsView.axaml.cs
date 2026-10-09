using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Songs;

public partial class SongsView : UserControl, IDisposable
{
    private readonly ShellViewModel? shell;
    private CancellationTokenSource? import;
    private bool closed;
    private SongTrackMenu? trackMenu;
    public SongsViewModel? Model { get; }
    public AlbumArtworkCache? Artwork => shell?.AlbumArtwork;
    public AppearanceVisualsViewModel? Appearance => shell?.AppearanceVisuals;
    public PlayerViewModel? Player => shell?.Player;
    public SongsView() => InitializeComponent();
    public SongsView(ShellViewModel shell) : this()
    {
        shell.AppearanceVisuals.PropertyChanged += AppearanceChanged; ApplyScrollbar(shell);
        this.shell = shell; Model = new(shell.TrackLibrary!, shell.Player, shell.Text, shell.QueryPreference("songs")); DataContext = Model;
        trackMenu = new(this, shell, Model); TrackList.ContextMenu = trackMenu.Menu;
        TrackList.AddHandler(InputElement.PointerPressedEvent, OnTrackPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        Model.PreferenceChanged += () => { try { shell.RememberQuery(Model.Preference); } catch (ArgumentException) { } };
        BindLayout();
        AttachedToVisualTree += (_, _) => _ = Model.LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    private void AppearanceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    { if (shell is not null && args.PropertyName == nameof(AppearanceVisualsViewModel.WaveformScrollbar)) ApplyScrollbar(shell); }
    public void FocusSearch() => SearchField.Focus();
    private void OnSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { _ = Model?.LoadAsync(); args.Handled = true; }
        if (args.Key == Key.Escape && Model is not null) { Model.Search = ""; args.Handled = true; }
    }
    private void OnAdvancedFilters(object? sender, RoutedEventArgs args)
    {
        if (FilterButton.Flyout is not Flyout flyout) return;
        if (flyout.Content is Echo.Desktop.Features.LibraryDetails.LibraryFilterView panel) panel.ShowAdvancedOptions();
        flyout.ShowAt(FilterButton);
    }
    private void OnLayoutPick(object? sender, RoutedEventArgs args)
    {
        if (Model is null || sender is not Control { Tag: string layout } || layout is not ("list" or "split" or "dense")) return;
        Model.Layout = layout; CloseLibraryMenu(); args.Handled = true;
    }
    private void OnCommand(object? sender, RoutedEventArgs args)
    {
        if (Model is null || sender is not Control { Tag: string token }) return;
        var query = Model.Search.TrimEnd(); Model.Search = query + (query.Length > 0 ? " " : "") + "/" + token + " ";
        CommandButton.Flyout?.Hide(); FocusSearch(); SearchField.CaretIndex = Model.Search.Length;
    }
    private void OnDoubleTapped(object? sender, TappedEventArgs args)
    {
        if (args.Source is Avalonia.Visual visual && visual.GetSelfAndVisualAncestors().OfType<Button>().Any()) return;
        Model?.PlayCommand.Execute(null);
    }
    private void OnTrackKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { Model?.PlayCommand.Execute(null); args.Handled = true; }
        if ((args.Key == Key.Apps || (args.Key == Key.F10 && args.KeyModifiers == KeyModifiers.Shift)) && Model?.Selected is { } row)
        {
            var anchor = TrackList.GetVisualDescendants().OfType<SongTrackRow>().FirstOrDefault(item => item.DataContext is TrackRow current && current.Id == row.Id);
            ShowTrackMenu(row, (Control?)anchor ?? TrackList); args.Handled = true;
        }
    }
    internal void ShowTrackMenu(TrackRow row, Control anchor) => trackMenu?.Show(row, anchor);
    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (Model is null || !args.GetCurrentPoint(TrackList).Properties.IsRightButtonPressed) return;
        trackMenu?.Prepare(null);
        if (args.Source is Avalonia.Visual visual && visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is TrackRow row)
        { Model.Selected = row; trackMenu?.Prepare(row); }
    }
    private async void OnImport(object? sender, RoutedEventArgs args)
    {
        if (shell?.ImportLibrary is null || import is not null || TopLevel.GetTopLevel(this) is not { } top) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new() { AllowMultiple = false, Title = shell.Text["query.import"] });
        if (closed || folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        using var current = new CancellationTokenSource(); import = current; CancelImport.IsVisible = true; ImportStatus.IsVisible = true;
        try
        {
            var progress = new Progress<ScanProgress>(state => { if (!closed) ImportStatus.Text = shell.Text.Format("query.scan", state.Processed, state.Failed); });
            await shell.ImportLibrary(path, progress, current.Token);
            if (!closed) { ImportStatus.Text = ""; ImportStatus.IsVisible = false; await Model!.LoadAsync(); }
        }
        catch (OperationCanceledException) { if (!closed) ImportStatus.Text = shell.Text["query.scanCancelled"]; }
        catch (Exception) { if (!closed) ImportStatus.Text = shell.Text["query.scanFailed"]; }
        finally { import = null; if (!closed) CancelImport.IsVisible = false; }
    }
    private void OnCancelImport(object? sender, RoutedEventArgs args) => import?.Cancel();
    public void Dispose()
    {
        if (closed) return; closed = true;
        DetachAlphabet();
        if (Model is not null) Model.PropertyChanged -= OnModelChanged;
        Workspace.SizeChanged -= OnWorkspaceSized;
        if (shell is not null) shell.AppearanceVisuals.PropertyChanged -= AppearanceChanged; import?.Cancel(); trackMenu?.Dispose(); trackMenu = null; TrackList.ContextMenu = null; Model?.Dispose();
    }
}
