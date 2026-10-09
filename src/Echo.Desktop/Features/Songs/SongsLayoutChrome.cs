using Avalonia;
using Avalonia.Controls;

namespace Echo.Desktop.Features.Songs;

public partial class SongsView
{
    public static readonly StyledProperty<string> LayoutProperty =
        AvaloniaProperty.Register<SongsView, string>(nameof(Layout), "list");
    public string Layout { get => GetValue(LayoutProperty); set => SetValue(LayoutProperty, value); }

    private void BindLayout()
    {
        if (Model is null) return;
        Model.PropertyChanged += OnModelChanged;
        Layout = Model.Layout;
        Workspace.SizeChanged += OnWorkspaceSized;
        Inspector.Artwork = Artwork;
        ApplyLayoutChrome();
        AttachAlphabet();
    }
    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SongsViewModel.Layout) || Model is null) return;
        Layout = Model.Layout; ApplyLayoutChrome();
    }
    private void OnWorkspaceSized(object? sender, SizeChangedEventArgs e) => ApplyLayoutChrome();
    internal void CloseLibraryMenu() => LibraryButton.Flyout?.Hide();
    private void ApplyLayoutChrome()
    {
        var layout = Model?.Layout ?? "list";
        TrackList.Classes.Set("dense", layout == "dense");
        TrackList.Classes.Set("split", layout == "split");
        var width = ListPane.Bounds.Width;
        var narrow = width > 0 && width < 650;
        DenseHeader.ColumnDefinitions = new ColumnDefinitions(narrow ? "*,*,56" : "*,*,*,110,56");
        DenseAlbumHeader.IsVisible = !narrow; DenseSpecHeader.IsVisible = !narrow;
        Grid.SetColumn(DenseDurationHeader, narrow ? 2 : 4);
        var split = layout == "split";
        var stack = split && Workspace.Bounds.Width > 0 && Workspace.Bounds.Width < 900;
        Inspector.IsVisible = split;
        if (!split)
        {
            Workspace.ColumnDefinitions = new ColumnDefinitions("*"); Workspace.RowDefinitions = new RowDefinitions("*");
            Grid.SetColumn(ListPane, 0); Grid.SetRow(ListPane, 0); Grid.SetColumn(Inspector, 0); Grid.SetRow(Inspector, 0);
            ListPane.MaxHeight = double.PositiveInfinity;
            return;
        }
        if (stack)
        {
            Workspace.ColumnDefinitions = new ColumnDefinitions("*"); Workspace.RowDefinitions = new RowDefinitions("*,Auto");
            Grid.SetColumn(ListPane, 0); Grid.SetRow(ListPane, 0); Grid.SetColumn(Inspector, 0); Grid.SetRow(Inspector, 1);
            ListPane.MaxHeight = 360; Inspector.MaxHeight = 420; Inspector.Margin = new Thickness(0, 14, 0, 0);
            Inspector.SetChrome(true);
            return;
        }
        Workspace.ColumnDefinitions = new ColumnDefinitions("*,320"); Workspace.RowDefinitions = new RowDefinitions("*");
        Grid.SetColumn(ListPane, 0); Grid.SetRow(ListPane, 0); Grid.SetColumn(Inspector, 1); Grid.SetRow(Inspector, 0);
        ListPane.MaxHeight = double.PositiveInfinity; Inspector.MaxHeight = double.PositiveInfinity; Inspector.Margin = new Thickness(0);
        Inspector.SetChrome(false);
    }
}
