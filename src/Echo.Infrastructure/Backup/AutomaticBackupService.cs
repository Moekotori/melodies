namespace Echo.Infrastructure.Backup;

/// <summary>One worker, one pending backup, bounded retention of files owned by this scheduler.</summary>
public sealed class AutomaticBackupService : IAsyncDisposable
{
    private readonly LocalBackupService backup;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task worker;
    private sealed record Schedule(bool Enabled, string Directory, TimeSpan Interval, int Retention);
    private Schedule schedule = new(false, "", TimeSpan.FromHours(24), 5);
    private DateTimeOffset next = DateTimeOffset.MaxValue;
    private readonly object gate = new();
    public event Action<string>? Status;
    public AutomaticBackupService(LocalBackupService backup) { this.backup = backup; worker = RunAsync(); }
    public Task ConfigureAsync(bool enabled, string directory, double hours, int retention, CancellationToken token)
        => Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            if (!double.IsFinite(hours) || hours is < 1 or > 168 || retention is < 1 or > 20) throw new ArgumentException("备份计划超出允许范围。");
            var path = enabled ? Path.GetFullPath(directory.Length > 0 ? directory : throw new IOException("请先选择备份目录。")) : directory;
            if (enabled) Directory.CreateDirectory(path);
            lock (gate) { schedule = new(enabled, path, TimeSpan.FromHours(hours), retention); next = enabled ? DateTimeOffset.UtcNow + schedule.Interval : DateTimeOffset.MaxValue; }
        }, token);
    private async Task RunAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token).ConfigureAwait(false))
            {
                Schedule active;
                lock (gate)
                {
                    active = schedule;
                    if (!active.Enabled || DateTimeOffset.UtcNow < next) continue;
                    next = DateTimeOffset.UtcNow + active.Interval;
                }
                try
                {
                    var destination = Path.Combine(active.Directory, "echo-auto-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".zip");
                    await backup.CreateAsync(destination, lifetime.Token).ConfigureAwait(false);
                    // Only scheduler-named files with exact timestamp shape are considered for retention.
                    var owned = Directory.EnumerateFiles(active.Directory, "echo-auto-*.zip", SearchOption.TopDirectoryOnly).Take(512)
                        .Where(path => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(path)[10..], "yyyyMMdd-HHmmss",
                            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                        .OrderDescending(StringComparer.Ordinal).ToArray();
                    foreach (var stale in owned.Skip(active.Retention).Take(32)) { lifetime.Token.ThrowIfCancellationRequested(); File.Delete(stale); }
                    Notify("自动一致性备份已完成。");
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
                catch (Exception) { Notify("自动备份失败，请检查目录与磁盘空间；本轮不会循环重试。"); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    private void Notify(string message)
    { if (Status is { } handlers) foreach (Action<string> handler in handlers.GetInvocationList()) try { handler(message); } catch { } }
    public async ValueTask DisposeAsync() { lifetime.Cancel(); await worker.ConfigureAwait(false); Status = null; lifetime.Dispose(); }
}
