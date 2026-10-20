using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Dsp;

public partial class DspView : UserControl, IDisposable
{
    private bool navigationRevealQueued;
    public DspViewModel? Model { get; private set; }
    public DspView()
    {
        InitializeComponent(); SizeChanged += (_, _) => ArrangePage();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter && (e.KeyModifiers & (Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Meta)) != 0
                && DataContext is DspViewModel { CanApply: true } model)
            { model.ApplyCommand.Execute(null); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => { if (Model is not null) Model.RefreshPageCommand.Execute(null); };
        DetachedFromVisualTree += (_, _) => Dispose();
    }
    public DspView(ShellViewModel shell) : this()
    {
        Model = new(shell.Player?.Session, shell.Player?.OutputName ?? "系统默认输出",
            new CommunityToolkit.Mvvm.Input.RelayCommand(() => shell.OpenQuickSettingsCommand.Execute("audio")));
        DataContext = Model;
    }
    private void ArrangePage()
    {
        var horizontal = Bounds.Width < 860;
        var narrow = Bounds.Width < 1040;
        Workbench.ColumnDefinitions = new(horizontal ? "*" : narrow ? "200,*" : "236,*");
        Workbench.RowDefinitions = new(horizontal ? "Auto,*" : "*");
        Grid.SetColumn(Workspace, horizontal ? 0 : 1); Grid.SetRow(Workspace, horizontal ? 1 : 0);
        NavigationSurface.BorderThickness = horizontal ? new Thickness(0, 0, 0, 1) : new Thickness(0, 0, 1, 0);
        NavigationLayout.Margin = horizontal ? new Thickness(10, 8) : new Thickness(12, 20, 12, 18);
        Brand.IsVisible = OutputSummary.IsVisible = !horizontal;
        ModuleList.Classes.Set("horizontal", horizontal);
        ScrollViewer.SetHorizontalScrollBarVisibility(ModuleList, horizontal ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(ModuleList, horizontal ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        Workspace.Margin = new Thickness(narrow ? 18 : 24, 0, narrow ? 18 : 24, 16);
        var stackHeader = Bounds.Width < 920;
        Header.ColumnDefinitions = new(stackHeader ? "*" : "*,Auto"); Header.RowDefinitions = new(stackHeader ? "Auto,Auto" : "Auto");
        Grid.SetColumn(HeaderTools, stackHeader ? 0 : 1); Grid.SetRow(HeaderTools, stackHeader ? 1 : 0);
        HeaderTools.Margin = stackHeader ? new Thickness(0, 12, 0, 0) : default;
        Footer.ColumnDefinitions = new(narrow ? "*" : "*,Auto"); Footer.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        var actions = Footer.Children[1]; Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetRow(actions, narrow ? 1 : 0);
        actions.Margin = narrow ? new Thickness(0, 10, 0, 0) : default;
        if (!navigationRevealQueued)
        {
            navigationRevealQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                navigationRevealQueued = false;
                if (ModuleList.SelectedItem is { } selected) ModuleList.ScrollIntoView(selected);
            }, DispatcherPriority.Loaded);
        }
    }
    public void Dispose() { Model?.Dispose(); Model = null; }
}
