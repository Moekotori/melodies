using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using System.ComponentModel;
using Echo.Desktop.Shell;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Remote;

public partial class RemoteView : UserControl, IDisposable
{
    public RemoteViewModel Model { get; }
    private bool restoreEditorFocus;
    private long restoreFocusRevision;
    private bool released;
    private readonly MotionLane directoryMotion = new();
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
        if (args.PropertyName == nameof(RemoteViewModel.DirectoryRevision)) RevealDirectory();
        if (args.PropertyName is nameof(RemoteViewModel.EditorOpen) or nameof(RemoteViewModel.ProviderPickerOpen))
        {
            if (Model.EditorOpen || Model.ProviderPickerOpen)
            {
                restoreEditorFocus = false;
                var revision = Echo.Desktop.Bootstrap.FocusPolicy.InputRevision(this);
                Dispatcher.UIThread.Post(() =>
                {
                    if (released || !(Model.EditorOpen || Model.ProviderPickerOpen)
                        || !Echo.Desktop.Bootstrap.FocusPolicy.IsCurrent(this, revision)) return;
                    Landing.FocusCurrent();
                }, DispatcherPriority.Input);
            }
            else { restoreEditorFocus = true; restoreFocusRevision = Echo.Desktop.Bootstrap.FocusPolicy.InputRevision(this); }
        }
        if (!restoreEditorFocus || Model.EditorOpen || Model.ProviderPickerOpen || Model.IsBusy) return;
        restoreEditorFocus = false;
        var returnRevision = restoreFocusRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (released || Model.EditorOpen || Model.ProviderPickerOpen || Model.IsBusy
                || !Echo.Desktop.Bootstrap.FocusPolicy.IsCurrent(this, returnRevision)) return;
            if (Model.HasSavedSources) AddSourceButton.Focus(); else Landing.FocusCurrent();
        }, DispatcherPriority.Input);
    }
    private async void RevealDirectory()
    {
        directoryMotion.Cancel();
        FileList.Opacity = 1; FileList.RenderTransform = MotionLane.At();
        if (Model.Entries.Count > 0) FileList.ScrollIntoView(Model.Entries[0]);
        var policy = MotionScope.GetPolicy(this);
        if (policy is not { CanAnimate: true }) return;
        policy.PropertyChanged += OnMotionPolicyChanged;
        try
        {
            await directoryMotion.PlayAsync(FileList, 0, 1,
                MotionLane.At(policy.UseSpatialMotion ? 8 : 0), MotionLane.At(), MotionTokens.Reveal);
        }
        finally { policy.PropertyChanged -= OnMotionPolicyChanged; }
    }
    private void OnMotionPolicyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MotionPolicy.CanAnimate)) return;
        directoryMotion.Cancel(); FileList.Opacity = 1; FileList.RenderTransform = MotionLane.At();
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
        directoryMotion.Dispose();
        Model.PropertyChanged -= OnModelChanged; Model.Dispose();
    }
}
