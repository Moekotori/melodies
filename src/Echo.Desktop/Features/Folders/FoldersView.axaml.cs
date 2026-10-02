using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Folders;

public partial class FoldersView : UserControl, IDisposable
{
    private ShellViewModel _shell = null!;
    private bool _closed;
    private bool _visible = true;
    private bool _compactTree;
    private bool _treeOpen;
    private double _dockedWidth = 208;
    public FoldersViewModel Model { get; private set; } = null!;
    public static readonly StyledProperty<Echo.Desktop.Features.Albums.AlbumArtworkCache?> ArtworkProperty =
        AvaloniaProperty.Register<FoldersView, Echo.Desktop.Features.Albums.AlbumArtworkCache?>(nameof(Artwork));
    public Echo.Desktop.Features.Albums.AlbumArtworkCache? Artwork
    { get => GetValue(ArtworkProperty); private set => SetValue(ArtworkProperty, value); }
    public FoldersView() { InitializeComponent(); }
    public FoldersView(ShellViewModel shell) : this()
    {
        _shell = shell; Model = new(shell); DataContext = Model;
        Artwork = shell.AlbumArtwork;
        Model.PropertyChanged += OnModelChanged;
        AttachedToVisualTree += (_, _) => _ = Model.InitializeAsync();
        DetachedFromVisualTree += (_, _) => Dispose();
        SizeChanged += (_, _) => ApplyLayout();
        MainPane.SizeChanged += (_, _) => ApplyLayout();
        KeyDown += (_, args) => { if (args.Key == Key.Escape && _treeOpen) { SetTreeOpen(false); args.Handled = true; } };
        ApplyLayout();
        ImportButton.IsEnabled = shell.ImportLibrary is not null;
        RescanButton.IsVisible = shell.ImportLibrary is not null;
    }
    private void ApplyLayout()
    {
        var narrow = Bounds.Width is > 0 and < 860;
        var compactTree = Bounds.Width is > 0 and < 730;
        if (compactTree != _compactTree)
        {
            if (compactTree) _dockedWidth = Math.Max(180, FolderLayout.ColumnDefinitions[0].Width.Value);
            _compactTree = compactTree; _treeOpen = false;
            FolderLayout.ColumnDefinitions[0].Width = new(compactTree ? 0 : _dockedWidth);
            FolderLayout.ColumnDefinitions[1].Width = new(compactTree ? 0 : 5);
            Grid.SetColumn(TreePane, compactTree ? 2 : 0);
            TreePane.Width = compactTree ? 240 : double.NaN;
            TreePane.HorizontalAlignment = compactTree ? Avalonia.Layout.HorizontalAlignment.Left : Avalonia.Layout.HorizontalAlignment.Stretch;
            TreePane.ZIndex = compactTree ? 3 : 0;
            TreePane.Classes.Set("directory-overlay", compactTree);
        }
        DirectoryToggle.IsVisible = compactTree; TreeSplitter.IsVisible = TreeDivider.IsVisible = !compactTree;
        TreePane.IsVisible = !compactTree || _treeOpen;
        TreeDismissLayer.IsVisible = compactTree && _treeOpen;
        TreePane.Margin = compactTree ? new(0) : new(0, 0, 18, 0);
        FolderLayout.ColumnDefinitions[0].MinWidth = compactTree ? 0 : 180;
        FolderLayout.ColumnDefinitions[0].MaxWidth = narrow ? 210 : 300;
        MainPane.Margin = new(compactTree ? 0 : 30, 0, 0, 0);
        var stackedActions = MainPane.Bounds.Width is > 0 and < 790 || compactTree;
        Hero.ColumnDefinitions[0].Width = new(narrow ? 80 : 100);
        CoverStack.Width = CoverStack.Height = narrow ? 76 : 96;
        Hero.ColumnSpacing = narrow ? 14 : 18;
        FolderTitle.FontSize = narrow ? 24 : 28;
        Grid.SetRow(ActionBar, stackedActions ? 1 : 0);
        Grid.SetColumn(ActionBar, stackedActions ? 1 : 2);
        Grid.SetColumnSpan(ActionBar, stackedActions ? 2 : 1);
        Hero.RowSpacing = stackedActions ? 8 : 0;
        ActionBar.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        // Keep search and sort usable in the minimum desktop window.
        LosslessToggle.IsVisible = !narrow;
        FilterBar.ColumnDefinitions[1].Width = new(narrow ? 0 : 1, narrow ? Avalonia.Controls.GridUnitType.Pixel : Avalonia.Controls.GridUnitType.Auto);
    }
    private void SetTreeOpen(bool value)
    { _treeOpen = value; TreePane.IsVisible = !_compactTree || value; TreeDismissLayer.IsVisible = _compactTree && value; if (value) FolderTree.Focus(); else DirectoryToggle.Focus(); }
    private void OnToggleTree(object? sender, RoutedEventArgs args) => SetTreeOpen(!_treeOpen);
    private void OnDismissTree(object? sender, PointerPressedEventArgs args) { SetTreeOpen(false); args.Handled = true; }
    public void FocusSearch() => SearchField.Focus();
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(FoldersViewModel.CurrentFolder)) { _coverSamples = []; ReleaseArtwork(); }
        if (args.PropertyName == nameof(FoldersViewModel.Tracks)) _ = LoadArtworkAsync();
    }
    private void OnExpand(object? sender, RoutedEventArgs args)
    { if (sender is Button { DataContext: FolderNode node }) _ = Model.Tree.ExpandAsync(node); args.Handled = true; }
    private void OnTreeKey(object? sender, KeyEventArgs args)
    {
        if (Model.SelectedNode is not { } node) return;
        if (args.Key == Key.Right && !node.IsExpanded || args.Key == Key.Left && node.IsExpanded)
        { _ = Model.Tree.ExpandAsync(node); args.Handled = true; }
        else if (args.Key == Key.Left && node.Parent is not null)
        { Model.SelectedNode = node.Parent; args.Handled = true; }
        else if (args.Key == Key.Enter && _compactTree && _treeOpen)
        { SetTreeOpen(false); args.Handled = true; }
        else if (args.Key == Key.Enter)
        { _ = Model.Tree.ExpandAsync(node); args.Handled = true; }
    }
    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs args)
    {
        if (!_compactTree || !_treeOpen || args.Source is not Visual source) return;
        var ancestors = source.GetVisualAncestors().ToArray();
        if (ancestors.Any(ancestor => ancestor is ListBoxItem) && !ancestors.Any(ancestor => ancestor is Button)) SetTreeOpen(false);
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
        Artwork = visible && !_closed ? _shell.AlbumArtwork : null;
        if (visible) _ = LoadArtworkAsync(); else ReleaseArtwork();
    }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true; _scan?.Cancel(); Artwork = null; ReleaseArtwork();
        Model.PropertyChanged -= OnModelChanged; Model.Dispose();
        TrackPanel.DataContext = null;
    }
}
