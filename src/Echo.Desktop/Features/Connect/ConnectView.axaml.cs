using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Connect.HqPlayer;
using Echo.Desktop.Features.Connect.Mobile;
using Echo.Desktop.Features.Connect.Output;
using Echo.Desktop.Features.Connect.Receive;
using Echo.Desktop.Features.Connect.SmartHome;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Connect;

public partial class ConnectView : UserControl, IDisposable
{
    private readonly ShellViewModel shell;
    private readonly Dictionary<string, Control> pages;
    private string? shownWorkspace;
    private int disposed;

    public ConnectViewModel Model { get; }

    public ConnectView(ShellViewModel shell)
    {
        this.shell = shell;
        Model = new ConnectViewModel(shell);
        InitializeComponent();
        DataContext = Model;
        pages = new Dictionary<string, Control>
        {
            ["output"] = new ConnectOutputView { DataContext = Model.Output },
            ["hqplayer"] = new ConnectHqPlayerView { DataContext = Model.HqPlayer },
            ["receive"] = new ConnectReceiveView { DataContext = Model.Receive },
            ["mobile"] = new ConnectMobileView { DataContext = Model.Mobile },
            ["smartHome"] = new ConnectSmartHomeView { DataContext = Model.SmartHome }
        };
        Model.PropertyChanged += OnModelChanged;
        ShowWorkspace();
        AttachedToVisualTree += (_, _) => Arrange();
        DetachedFromVisualTree += (_, _) => Dispose();
        SizeChanged += (_, _) => Arrange();
    }

    public void ApplyLaunchRequest()
    {
        var key = shell.TakeWorkspaceRequest();
        if (!string.IsNullOrEmpty(key)) Model.Select(key);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Model.PropertyChanged -= OnModelChanged;
        Model.Dispose();
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectViewModel.Selected)) ShowWorkspace();
    }

    // Workspaces are retained; the rail order decides which side the next one enters from.
    private void ShowWorkspace()
    {
        var key = Model.Selected.Mode.Key;
        var direction = shownWorkspace is null || shownWorkspace == key ? Motion.MotionDirection.None
            : Order(key) > Order(shownWorkspace) ? Motion.MotionDirection.Forward : Motion.MotionDirection.Back;
        shownWorkspace = key;
        if (pages.TryGetValue(key, out var page)) WorkspaceHost.Present(page, direction);
        if (Model.Selected.Mode.Key == "output") Model.Output.EnsureLoaded();
    }

    private int Order(string key)
    {
        for (var i = 0; i < Model.Items.Count; i++) if (Model.Items[i].Mode.Key == key) return i;
        return -1;
    }

    private void OnRailKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End) || e.KeyModifiers != KeyModifiers.None) return;
        var index = 0;
        for (var i = 0; i < Model.Items.Count; i++) if (Model.Items[i].IsSelected) index = i;
        var next = e.Key switch
        {
            Key.Home => 0,
            Key.End => Model.Items.Count - 1,
            Key.Down => (index + 1) % Model.Items.Count,
            _ => (index - 1 + Model.Items.Count) % Model.Items.Count
        };
        Model.Select(Model.Items[next].Mode.Key);
        e.Handled = true;
        Dispatcher.UIThread.Post(FocusSelected);
    }

    private void FocusSelected()
    {
        if (this.FindControl<ItemsControl>("WorkspaceList") is not { } list) return;
        foreach (var button in list.GetVisualDescendants().OfType<Button>())
            if (button.DataContext is ConnectWorkspaceItem { IsSelected: true }) { button.Focus(); return; }
    }

    private void Arrange()
    {
        if (Stage is null || Rail is null || Desk is null || Header is null || HeaderActions is null) return;
        var narrow = Bounds.Width < 860;
        Stage.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "260,*");
        Stage.RowDefinitions = new RowDefinitions(narrow ? "Auto,*" : "*");
        Grid.SetColumn(Rail, 0);
        Grid.SetRow(Rail, 0);
        Grid.SetColumn(Desk, narrow ? 0 : 1);
        Grid.SetRow(Desk, narrow ? 1 : 0);
        Rail.MaxHeight = narrow ? 188 : double.PositiveInfinity;
        Rail.BorderThickness = narrow ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
        Desk.Margin = narrow ? new Thickness(16, 16, 12, 8) : new Thickness(28, 22, 22, 8);
        var stack = Bounds.Width < 720;
        Header.ColumnDefinitions = new ColumnDefinitions(stack ? "*" : "*,Auto");
        Header.RowDefinitions = new RowDefinitions(stack ? "Auto,Auto" : "Auto");
        Grid.SetColumn(HeaderActions, stack ? 0 : 1);
        Grid.SetRow(HeaderActions, stack ? 1 : 0);
        HeaderActions.Margin = stack ? new Thickness(0, 12, 0, 0) : default;
        Grid.SetRow(ConfirmLayer, 0);
        Grid.SetColumn(ConfirmLayer, 0);
        Grid.SetRowSpan(ConfirmLayer, narrow ? 2 : 1);
        Grid.SetColumnSpan(ConfirmLayer, narrow ? 1 : 2);
    }
}
