using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Echo.Desktop.Features.Remote;

public partial class RemoteConnectionEditor : UserControl
{
    private bool addressHasFocus;
    private bool focusQueued;
    private TextBox AddressField => DataContext is RemoteViewModel { IsOneDrive: true } ? ClientIdField : UrlField;
    public RemoteConnectionEditor()
    {
        InitializeComponent();
        GotFocus += (_, args) => { if (args.Source is TextBox input) addressHasFocus = input == AddressField; };
        UrlField.TemplateApplied += (_, _) => { if (addressHasFocus) FocusAddress(); };
        UrlField.LostFocus += (_, _) =>
        {
            if (addressHasFocus && TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is null)
                FocusAddress();
        };
    }
    public void FocusAddress()
    {
        if (focusQueued) return;
        focusQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            focusQueued = false;
            var field = AddressField;
            if (!field.IsEffectivelyVisible || !field.IsEnabled || TopLevel.GetTopLevel(this) is not { } top) return;
            // Complete visibility/template changes first, and preserve deliberate focus on another field.
            if (top.FocusManager?.GetFocusedElement() is TextBox input && input != field) return;
            field.Focus();
        }, DispatcherPriority.ContextIdle);
    }
    private async void OnPickMount(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not RemoteViewModel model || !model.IsMounted || model.IsBusy || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var folders = await top.StorageProvider.OpenFolderPickerAsync(new() { Title = model.AddressLabel, AllowMultiple = false });
            if (TopLevel.GetTopLevel(this) != top || !model.EditorOpen || !model.IsMounted) return;
            if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) model.DraftUrl = path;
        }
        catch (Exception) { if (model.EditorOpen) model.EditorError = "无法打开文件夹选择器，请填写已挂载的目录路径。"; }
    }
    private async void OnOneDriveLogin(object? sender, RoutedEventArgs args)
    {
        if (DataContext is not RemoteViewModel model || !model.IsOneDrive || model.IsBusy || TopLevel.GetTopLevel(this) is not { } top) return;
        await model.AuthorizeOneDriveAsync(async (uri, token) =>
        {
            var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (!model.EditorOpen || TopLevel.GetTopLevel(this) != top) { opened.TrySetCanceled(); return; }
                    var launched = await top.Launcher.LaunchUriAsync(uri); token.ThrowIfCancellationRequested();
                    if (!launched) throw new IOException("无法打开浏览器，请检查系统默认浏览器设置。");
                    opened.TrySetResult();
                }
                catch (OperationCanceledException) { opened.TrySetCanceled(token); }
                catch (Exception error) { opened.TrySetException(error); }
            });
            await opened.Task.WaitAsync(token);
        });
    }
}
