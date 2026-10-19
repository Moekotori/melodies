using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Application.Playback;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Settings.Runtime;
using Echo.Desktop.Localization;
using Echo.Playback;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerToolsViewModel : ObservableObject, IDisposable
{
    private readonly IPlaybackSession session;
    private readonly ISleepTimer timer;
    private readonly FeaturePreferences preferences;
    private readonly LocalizationService text;
    private readonly CancellationTokenSource lifetime = new();
    private string? itemId;
    private string? observedItem;
    private bool observedAvailable;
    private bool synchronizing;
    private int queued, disposed;
    [ObservableProperty] private double minutes = 30;
    [ObservableProperty] private int actionIndex;
    [ObservableProperty] private double startSeconds;
    [ObservableProperty] private double endSeconds;
    [ObservableProperty] private double trackDuration;
    [ObservableProperty] private bool canLoop;
    [ObservableProperty] private bool loopActive;
    [ObservableProperty] private bool timerActive;
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string timerStatus = "";
    [ObservableProperty] private string loopStatus = "";
    [ObservableProperty] private string feedback = "";
    [ObservableProperty] private IReadOnlyList<string> actionLabels = [];

    public PlayerToolsViewModel(IPlaybackSession session, ISleepTimer timer,
        FeaturePreferences preferences, LocalizationService text)
    {
        this.session = session; this.timer = timer; this.preferences = preferences; this.text = text;
        session.Changed += OnPlayback; session.SegmentLoopChanged += OnLoop;
        timer.Changed += OnTimer; preferences.Changed += OnPreferences; text.Changed += RefreshLanguage;
        RestorePreferences(); RefreshLanguage();
    }

    partial void OnMinutesChanged(double value)
    {
        if (!synchronizing && double.IsFinite(value) && value is >= 1 and <= 720)
            preferences.Confirm("sleep-timer-minutes", new(false, value.ToString(System.Globalization.CultureInfo.InvariantCulture), value));
    }
    partial void OnActionIndexChanged(int value)
    {
        if (!synchronizing && value is >= 0 and <= 2)
            preferences.Confirm("sleep-timer-action", new(false, value switch { 1 => "停止", 2 => "退出应用", _ => "暂停" }));
    }
    private void OnPreferences(string id)
    { if (id.Length == 0 || id.StartsWith("sleep-timer-", StringComparison.Ordinal)) RestorePreferences(); }
    private void RestorePreferences()
    {
        synchronizing = true;
        try
        {
            var saved = preferences.Read("sleep-timer-minutes", new(false, "30", 30));
            Minutes = double.TryParse(saved.Selection, System.Globalization.CultureInfo.InvariantCulture, out var restored)
                && restored is >= 1 and <= 720 ? restored : 30;
            ActionIndex = preferences.Read("sleep-timer-action", new(false, "暂停")).Selection switch { "停止" => 1, "退出应用" => 2, _ => 0 };
        }
        finally { synchronizing = false; }
    }

    private void OnPlayback(PlaybackSnapshot value)
    {
        var available = Available(value);
        if (observedItem == value.Current?.ItemId && observedAvailable == available) return;
        observedItem = value.Current?.ItemId; observedAvailable = available; QueueRefresh();
    }
    private void OnLoop(SegmentLoopState? _) => QueueRefresh();
    private void OnTimer(SleepTimerState _) => QueueRefresh();
    private void RefreshLanguage()
    {
        var selected = ActionIndex;
        synchronizing = true;
        try
        {
            ActionLabels = [text["playerTools.pause"], text["playerTools.stop"], text["playerTools.quit"]];
            ActionIndex = selected;
            OnPropertyChanged(nameof(ActionIndex));
        }
        finally { synchronizing = false; }
        Refresh();
    }
    private void QueueRefresh()
    {
        if (Volatile.Read(ref disposed) != 0 || Interlocked.Exchange(ref queued, 1) != 0) return;
        Dispatcher.UIThread.Post(() => { Interlocked.Exchange(ref queued, 0); if (disposed == 0) Refresh(); });
    }
    private static bool Available(PlaybackSnapshot state) => state.OperationId > 0 && state.DurationSeconds > 0.75
        && state.Current?.Metadata?.GetValueOrDefault("isLiveStream") != "true"
        && state.Status is PlaybackStatus.Playing or PlaybackStatus.Paused or PlaybackStatus.Loading;
    private void Refresh()
    {
        var playback = session.Snapshot;
        if (itemId != playback.Current?.ItemId)
        { itemId = playback.Current?.ItemId; StartSeconds = 0; EndSeconds = Math.Max(0, playback.DurationSeconds - 0.25); }
        else if (!CanLoop && Available(playback)) EndSeconds = Math.Max(0, playback.DurationSeconds - 0.25);
        CanLoop = Available(playback); TrackDuration = Math.Max(0, playback.DurationSeconds - 0.25);
        LoopActive = session.SegmentLoop is not null;
        LoopStatus = session.SegmentLoop is { } loop ? $"A {Format(loop.StartSeconds)}  →  B {Format(loop.EndSeconds)}"
            : text["playerTools.loopIdle"];
        var current = timer.State;
        TimerActive = current.Active;
        TimerStatus = current.Executing ? text["playerTools.executing"] : current.Active
            ? text.Format("playerTools.remaining", Format(Math.Ceiling(current.RemainingSeconds))) : text["playerTools.timerIdle"];
        if (current.Error is { } error) Feedback = text.Translate(error);
    }
    private static string Format(double seconds) => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");

    [RelayCommand] private Task StartTimerAsync() => RunAsync(() => timer.StartAsync(Minutes, (SleepTimerAction)ActionIndex, lifetime.Token));
    [RelayCommand] private Task CancelTimerAsync() => RunAsync(() => timer.CancelAsync(lifetime.Token));
    [RelayCommand] private void MarkStart() { if (CanLoop) StartSeconds = Math.Min(TrackDuration, session.Snapshot.PositionSeconds); }
    [RelayCommand] private void MarkEnd() { if (CanLoop) EndSeconds = Math.Min(TrackDuration, session.Snapshot.PositionSeconds); }
    [RelayCommand] private Task StartLoopAsync() => RunAsync(() => session.SetSegmentLoopAsync(itemId ?? "", StartSeconds, EndSeconds, lifetime.Token));
    [RelayCommand] private Task ClearLoopAsync() => RunAsync(() => session.ClearSegmentLoopAsync(lifetime.Token));
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy || disposed != 0) return;
        IsBusy = true; Feedback = "";
        try { await action(); }
        catch (OperationCanceledException) when (disposed != 0) { }
        catch (Exception error) { if (disposed == 0) Feedback = text.Translate(error.Message); }
        finally { if (disposed == 0) { IsBusy = false; Refresh(); } }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        session.Changed -= OnPlayback; session.SegmentLoopChanged -= OnLoop;
        timer.Changed -= OnTimer; preferences.Changed -= OnPreferences; text.Changed -= RefreshLanguage;
        lifetime.Cancel(); lifetime.Dispose();
    }
}
