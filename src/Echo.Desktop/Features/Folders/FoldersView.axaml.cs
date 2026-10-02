using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Folders;

public partial class FoldersView : UserControl, IDisposable
{
    private ShellViewModel _shell = null!;
    private bool _closed;
    private bool _visible = true;
    public FoldersViewModel Model { get; private set; } = null!;
    public FoldersView() { InitializeComponent(); }
    public FoldersView(ShellViewModel shell) : this()
    {
        _shell = shell; Model = new(shell); DataContext = Model;
        Model.PropertyChanged += OnModelChanged;
        AttachedToVisualTree += (_, _) => _ = Model.InitializeAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
        SizeChanged += (_, _) => ApplyLayout();
        ApplyLayout();
        ImportButton.IsEnabled = shell.ImportLibrary is not null;
        RescanButton.IsVisible = shell.ImportLibrary is not null;
    }
    private void ApplyLayout()
    {
        var narrow = Bounds.Width is > 0 and < 840;
        FolderLayout.ColumnDefinitions[0].MinWidth = narrow ? 132 : 180;
        FolderLayout.ColumnDefinitions[0].MaxWidth = narrow ? 180 : 340;
        if (narrow && FolderLayout.ColumnDefinitions[0].Width.Value > 180) FolderLayout.ColumnDefinitions[0].Width = new(150);
        MainPane.Margin = new(narrow ? 14 : 24, 0, 0, 0);
        Hero.ColumnDefinitions[0].Width = new(narrow ? 76 : 104);
        CoverStack.Width = CoverStack.Height = narrow ? 72 : 100;
        Hero.ColumnSpacing = narrow ? 12 : 18;
        // Keep search and sort usable in the minimum desktop window.
        LosslessToggle.IsVisible = !narrow;
        FilterBar.ColumnDefinitions[1].Width = new(narrow ? 0 : 1, narrow ? Avalonia.Controls.GridUnitType.Pixel : Avalonia.Controls.GridUnitType.Auto);
    }
    public void FocusSearch() => SearchField.Focus();
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(FoldersViewModel.Tracks)) _ = LoadArtworkAsync(); }
    private void OnExpand(object? sender, RoutedEventArgs args)
    { if (sender is Button { DataContext: FolderNode node }) _ = Model.Tree.ExpandAsync(node); args.Handled = true; }
    private void OnTreeKey(object? sender, KeyEventArgs args)
    {
        if (Model.SelectedNode is not { } node) return;
        if (args.Key == Key.Right && !node.IsExpanded || args.Key == Key.Left && node.IsExpanded)
        { _ = Model.Tree.ExpandAsync(node); args.Handled = true; }
        else if (args.Key == Key.Left && node.Parent is not null)
        { Model.SelectedNode = node.Parent; args.Handled = true; }
        else if (args.Key == Key.Enter)
        { _ = Model.Tree.ExpandAsync(node); args.Handled = true; }
    }
    private void OnSearchKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Enter) { _ = Model.LoadAsync(); args.Handled = true; } }
    private void OnClearSearch(object? sender, RoutedEventArgs args) { Model.Search = ""; FocusSearch(); }
    private void OnBreadcrumb(object? sender, RoutedEventArgs args)
    { if (sender is Button { Tag: FolderRow row }) Model.SelectBreadcrumb(row); }
    private void OnOpenFolder(object? sender, RoutedEventArgs args) => _ = Model.OpenFolderAsync();
    private async void OnCopyPath(object? sender, RoutedEventArgs args)
    {
        if (Model.CurrentFolder is null || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        try { await clipboard.SetValueAsync(DataFormat.Text, Model.CurrentFolder.Path); }
        catch (Exception) { if (!_closed) Model.Error = Model.Text["folders.copyError"]; }
    }
    public void SetWindowVisible(bool visible)
    {
        _visible = visible;
        if (visible) _ = LoadArtworkAsync(); else ReleaseArtwork();
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _scan?.Cancel(); ReleaseArtwork();
        Model.PropertyChanged -= OnModelChanged; Model.Dispose();
        TrackPanel.DataContext = null;
    }
}
