using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System.ComponentModel;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Remote;

public partial class RemoteView : UserControl, IDisposable
{
    public RemoteViewModel Model { get; }
    private bool restoreEditorFocus;
    private bool released;
    public RemoteView(ShellViewModel shell)
    {
        InitializeComponent(); Model = new(shell); DataContext = Model;
        Model.PropertyChanged += OnModelChanged;
        AttachedToVisualTree += (_, _) => { _ = Model.InitializeAsync(); };
        DetachedFromVisualTree += (_, _) => Dispose();
        SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 780;
            SourcePicker.Width = compact ? 170 : 220; SearchHost.Width = compact ? 200 : 260;
            SourceAddressText.IsVisible = !compact; AddSourceText.IsVisible = !compact;
        };
        KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape && Model.EditorOpen) { Model.CloseEditorCommand.Execute(null); args.Handled = true; }
            else if (args.Key == Key.Escape && Model.ProviderPickerOpen) { Model.CloseProviderPickerCommand.Execute(null); args.Handled = true; }
            else if (args.Key == Key.Escape && Model.ConfirmRemove) { Model.CancelRemoveCommand.Execute(null); args.Handled = true; }
        };
    }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(RemoteViewModel.EditorOpen) or nameof(RemoteViewModel.ProviderPickerOpen))
        {
            if (Model.EditorOpen || Model.ProviderPickerOpen)
            {
                restoreEditorFocus = false;
                Dispatcher.UIThread.Post(() =>
                {
                    if (released) return;
                    if (Model.EditorOpen) ConnectionEditor.FocusAddress(); else if (Model.ProviderPickerOpen) ProviderPicker.FocusFirst();
                }, DispatcherPriority.Input);
            }
            else restoreEditorFocus = true;
        }
        if (!restoreEditorFocus || Model.EditorOpen || Model.ProviderPickerOpen || Model.IsBusy) return;
        restoreEditorFocus = false;
        Dispatcher.UIThread.Post(() =>
        {
            if (released || Model.EditorOpen || Model.ProviderPickerOpen || Model.IsBusy) return;
            if (Model.HasSavedSources) AddSourceButton.Focus(); else Welcome.FocusConnect();
        }, DispatcherPriority.Input);
    }
    private void OnSearchKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Enter && !Model.IsBusy) { Model.RefreshCommand.Execute(null); args.Handled = true; } }
    private void OnEntryActivated(object? sender, TappedEventArgs args) => Activate();
    private void OnEntryKey(object? sender, KeyEventArgs args)
    { if (args.Key == Key.Enter) { Activate(); args.Handled = true; } }
    private void Activate()
    {
        if (Model.CanOpen) Model.OpenCommand.Execute(null);
        else if (Model.CanPlay) Model.PlayCommand.Execute(null);
    }
    public void Dispose()
    {
        if (released) return; released = true;
        Model.PropertyChanged -= OnModelChanged; Model.Dispose();
    }
}
