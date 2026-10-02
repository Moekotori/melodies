using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Echo.Desktop.Features.Settings;

// Thirteen bounded category buttons; no data/IO owner, timers or retained pages.
public partial class SettingsNavigationView : UserControl
{
    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<SettingsNavigationView, bool>(nameof(IsCompact));
    public bool IsCompact { get => GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
    private SettingsViewModel? _model;
    private bool _scrollPosted;
    public SettingsNavigationView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Attach();
        DetachedFromVisualTree += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (this.IsAttachedToVisualTree()) Attach(); };
        KeyDown += OnNavigationKey;
    }
    private void Attach()
    {
        Detach(); _model = DataContext as SettingsViewModel;
        if (_model is not null) _model.PropertyChanged += OnModelChanged;
        RevealActive();
    }
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCompactProperty) { Classes.Set("compact", IsCompact); RevealActive(); }
    }
    private void Detach()
    { if (_model is not null) _model.PropertyChanged -= OnModelChanged; _model = null; }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SettingsViewModel.ActiveCategory) or nameof(SettingsViewModel.NavigationGroups)) RevealActive();
    }
    private Button[] Buttons() => this.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("settings-nav-item")).ToArray();
    private void RevealActive()
    {
        if (_scrollPosted) return; _scrollPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _scrollPosted = false;
            if (_model is not null) Buttons().FirstOrDefault(button => button.CommandParameter as string == _model.ActiveCategory.Id)?.BringIntoView();
        }, DispatcherPriority.Loaded);
    }
    private void OnNavigationKey(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.KeyModifiers != KeyModifiers.None || _model is null || args.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) return;
        var buttons = Buttons();
        if (buttons.Length == 0) return;
        var index = Array.FindIndex(buttons, button => button.IsKeyboardFocusWithin);
        if (index < 0) return;
        index = args.Key switch { Key.Home => 0, Key.End => buttons.Length - 1, Key.Up => Math.Max(0, index - 1), _ => Math.Min(buttons.Length - 1, index + 1) };
        buttons[index].Focus(NavigationMethod.Directional); buttons[index].BringIntoView();
        _model.SelectCategoryCommand.Execute(buttons[index].CommandParameter);
        args.Handled = true;
    }
}
