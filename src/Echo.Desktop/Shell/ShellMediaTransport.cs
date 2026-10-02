using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    private Action? retryMediaTransport;
    [ObservableProperty] private string mediaTransportError = "";
    [ObservableProperty] private string mediaTransportErrorDisplay = "";
    [ObservableProperty] private bool hasMediaTransportError;
    public void AttachMediaTransport(Action retry) => retryMediaTransport = retry;
    public void SetMediaTransportDiagnostic(string? message) => Dispatcher.UIThread.Post(() =>
    {
        MediaTransportError = message ?? "";
        if (message is not null) MediaTransportErrorDisplay = message;
        HasMediaTransportError = message is not null;
    });
    [RelayCommand] private void RetryMediaTransport() => retryMediaTransport?.Invoke();
}
