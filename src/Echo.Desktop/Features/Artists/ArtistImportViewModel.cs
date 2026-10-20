using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Contracts.Library;
using Echo.Core.Contracts;

namespace Echo.Desktop.Features.Artists;

public sealed partial class ArtistImportViewModel(IArtistCreditLibrary library, ICoreTaskControl tasks) : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private long revision;
    private Guid job;
    private bool closed;
    private bool loaded;
    [ObservableProperty] private string delimiters = ";";
    [ObservableProperty] private string status = "";
    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(SaveCommand)), NotifyCanExecuteChangedFor(nameof(ResetCommand))] private bool busy;
    [ObservableProperty] private int processed;
    [ObservableProperty] private int total = 1;
    public event Action? Rebuilt;
    public async Task LoadAsync()
    {
        if (loaded || closed) return;
        loaded = true; Busy = true;
        try
        {
            await ReadSettingsAsync();
            var running = (await tasks.TasksAsync(lifetime.Token)).FirstOrDefault(task => task.Kind == "artist-import" && task.Status == "running");
            if (running is not null) { job = running.Id; Busy = true; await FollowAsync(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Status = error.Message; }
        finally { Busy = false; }
    }
    private async Task ReadSettingsAsync()
    {
        var settings = await library.GetArtistImportSettingsAsync(lifetime.Token);
        revision = settings.Revision; Delimiters = string.Join('\n', settings.Delimiters);
    }
    private bool CanSave() => !Busy;
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        Busy = true; Status = "";
        try
        {
            var values = Delimiters.Replace("\r", "").Split('\n').Where(value => value.Length > 0).ToArray();
            if (values.Length > 16 || values.Any(value => value.Length > 16 || value.Contains('\0')))
                throw new ArgumentException("最多 16 个分隔符，每个最多 16 字符；NUL 始终保留。");
            var accepted = await tasks.StartTaskAsync(new("", ArtistImport: new(revision, values)), lifetime.Token);
            job = accepted.Id; await FollowAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Status = error.Message; }
        finally { Busy = false; }
    }
    private async Task FollowAsync()
    {
        try
        {
            while (true)
            {
                var state = await tasks.GetTaskAsync(job, lifetime.Token);
                if (state.ArtistImportProgress is { } progress) { Processed = progress.Processed; Total = Math.Max(1,progress.Total); }
                Status = state.Status switch { "completed" => "已应用", "canceled" => "已取消，原设置保留", "failed" => state.Error ?? "更新失败", _ => $"Core 正在更新署名索引 · {Processed:N0}/{Total:N0}" };
                if (state.Status != "running")
                {
                    await ReadSettingsAsync();
                    if (state.Status == "completed" && (state.Kind != "artist-import" || library is not IArtistImportNotifications)) Rebuilt?.Invoke();
                    break;
                }
                await Task.Delay(400,lifetime.Token);
            }
        }
        finally { Busy = false; }
    }
    [RelayCommand(CanExecute = nameof(CanSave))] private async Task ResetAsync() { Delimiters = ";"; await SaveAsync(); }
    [RelayCommand] private async Task CancelAsync()
    {
        if (job == Guid.Empty) return;
        try { await tasks.CancelTaskAsync(job,lifetime.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { Status = error.Message; }
    }
    [RelayCommand] private async Task ReadTagsAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            var accepted = await tasks.StartTaskAsync(new("", Maintenance: "重新读取全部歌曲"),lifetime.Token);
            job = accepted.Id; await FollowAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { Status = error.Message; }
        finally { Busy = false; }
    }
    public void Dispose() { if (closed) return; closed = true; lifetime.Cancel(); lifetime.Dispose(); Rebuilt = null; }
}
