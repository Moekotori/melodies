using Avalonia;
using Avalonia.Controls;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Dsp;

public partial class DspView : UserControl, IDisposable
{
    public DspViewModel? Model { get; private set; }
    public DspView()
    {
        InitializeComponent(); SizeChanged += (_, _) => ArrangePage();
        AttachedToVisualTree += (_, _) => { if (Model is not null) Model.RefreshCommand.Execute(null); };
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
        var narrow = Bounds.Width < 700;
        Workbench.ColumnDefinitions[0].Width = new GridLength(narrow ? 150 : 178);
        Workspace.Margin = new Thickness(narrow ? 18 : 32, 24, narrow ? 16 : 30, 18);
        var stackHeader = Bounds.Width < 620;
        Header.ColumnDefinitions = new(stackHeader ? "*" : "*,Auto"); Header.RowDefinitions = new(stackHeader ? "Auto,Auto" : "Auto");
        Grid.SetColumn(HeaderTools, stackHeader ? 0 : 1); Grid.SetRow(HeaderTools, stackHeader ? 1 : 0);
        HeaderTools.Margin = stackHeader ? new Thickness(0, 12, 0, 0) : default;
        Footer.ColumnDefinitions = new(narrow ? "*" : "*,Auto"); Footer.RowDefinitions = new(narrow ? "Auto,Auto" : "Auto");
        var actions = Footer.Children[1]; Grid.SetColumn(actions, narrow ? 0 : 1); Grid.SetRow(actions, narrow ? 1 : 0);
        actions.Margin = narrow ? new Thickness(0, 10, 0, 0) : default;
    }
    public void Dispose() { Model?.Dispose(); Model = null; }
}
