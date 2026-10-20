using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.History;

public partial class HistoryView : UserControl, IDisposable
{
    private readonly ShellViewModel? shell;
    public HistoryViewModel? Model { get; }
    public Echo.Desktop.Features.Albums.AlbumArtworkCache? Artwork => shell?.AlbumArtwork;
    private bool closed;
    public HistoryView()
    {
        InitializeComponent();
        SizeChanged += (_, _) =>
        {
            var compact = Bounds.Width < 780;
            RecordsColumns.ColumnDefinitions = new(compact ? "64,3*,0,140,28" : "64,3*,2*,150,28");
            AlbumColumn.IsVisible = !compact;
        };
    }
    public HistoryView(ShellViewModel shell) : this()
    {
        this.shell = shell; Model = new(shell.HistoryLibrary!, shell.Player, shell.Text); DataContext = Model;
        Model.DataChanged += shell.NotifyHomeDataChanged;
        AttachedToVisualTree += (_, _) => _ = Model.LoadAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    public void FocusSearch() => SearchField.Focus();
    private void OnRange(object? sender, RoutedEventArgs args)
    { if (sender is Button { Tag: int index } && Model is not null) Model.RangeIndex = index; }
    private void OnSearchKey(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter) { _ = Model?.LoadAsync(reset: true); args.Handled = true; }
        if (args.Key == Key.Escape && Model is not null) { Model.Search = ""; args.Handled = true; }
    }
    private void OnPlay(object? sender, TappedEventArgs args)
    {
        if (args.Source is Button || (args.Source as Avalonia.Visual)?.GetVisualAncestors().OfType<Button>().Any() == true) return;
        if (args.Source is Avalonia.Visual source && source.GetSelfAndVisualAncestors().OfType<ScrollBar>().Any()) return;
        if ((args.Source as Avalonia.Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>().FirstOrDefault()?.DataContext is not HistoryRow row) return;
        RecordsList.SelectedItem = row; Model?.PlayCommand.Execute(null);
    }
    private void OnRecordsKey(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Source is Button || (args.Source as Avalonia.Visual)?.GetVisualAncestors().OfType<Button>().Any() == true) return;
        if (args.Key == Key.Enter) { Model?.PlayCommand.Execute(null); args.Handled = true; }
    }
    private void OnRecordPointer(object? sender, PointerPressedEventArgs args)
    {
        if (args.GetCurrentPoint(this).Properties.IsRightButtonPressed && args.Source is Avalonia.Visual visual)
        {
            var item = visual.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
            if (item?.DataContext is HistoryRow row) RecordsList.SelectedItem = row;
        }
    }
    private void OnMenuPlay(object? sender, RoutedEventArgs args) => Model?.PlayCommand.Execute(null);
    private void OnMenuNext(object? sender, RoutedEventArgs args) => Model?.PlayNextCommand.Execute(null);
    private void OnMenuAdd(object? sender, RoutedEventArgs args) => Model?.AddCommand.Execute(null);
    private async void OnCopy(object? sender, RoutedEventArgs args)
    {
        if (Model?.Selected is not { } row || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try { await clipboard.SetTextAsync(row.Record.Track.Title + " — " + row.Record.Track.Artist); if (!closed) Model.Message = Model.Text["history.copied"]; }
        catch (Exception) { if (!closed) Model.Error = Model.Text["history.copyFailed"]; }
    }
    private async void OnShowFolder(object? sender, RoutedEventArgs args)
    {
        if (Model?.Selected is not { } row || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var path = row.Record.Track.Source?.AudioPath ?? row.Record.Track.Path;
            var directory = await top.StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(path)!);
            if (directory is null || !await top.Launcher.LaunchUriAsync(directory.Path)) throw new IOException();
        }
        catch (Exception) { if (!closed && Model is not null) Model.Error = Model.Text["history.folderFailed"]; }
    }
    public void Dispose() { if (closed) return; closed = true; Model?.Dispose(); }
}
