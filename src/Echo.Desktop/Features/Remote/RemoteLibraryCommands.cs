using CommunityToolkit.Mvvm.Input;
using Echo.Application.Remote;
using Echo.Contracts.Remote;

namespace Echo.Desktop.Features.Remote;

public sealed partial class RemoteViewModel
{
    [RelayCommand]
    private Task SyncAsync() => RunAsync(async token =>
    {
        if (SelectedSource is not { SyncMode: "index" } selected || shell.RemoteFiles is null || shell.RemoteIndex is null)
            throw new IOException(shell.RemoteIndex is null ? "曲库尚未就绪，无法索引网盘。" : "这个来源只浏览和播放，不会写入曲库。");
        var sync = shell.RemoteIndex as IRemoteIndexSync ?? throw new InvalidOperationException("Core 索引服务未连接。");
        var progress = new Progress<RemoteSyncProgress>(item => Message = shell.Text.Format("remote.syncing", item.Discovered, item.Inserted, item.Updated));
        var result = await sync.SyncSourceAsync(selected.Id, progress, token);
        token.ThrowIfCancellationRequested();
        if (!disposed) Message = shell.Text.Format("remote.synced", result.Inserted, result.Updated, result.Unchanged, result.Missing);
    });
}
