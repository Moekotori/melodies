using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Channels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Echo.Contracts.Media;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Media;
using Echo.Playback;

namespace Echo.Desktop.Bootstrap;

internal sealed class WindowAppearanceVisualBinding : IDisposable
{
    private sealed record Request(string Path, double Duration, CancellationToken Cancellation);
    private readonly Window window;
    private readonly ShellViewModel shell;
    private readonly PlayerViewModel? player;
    private readonly IAppearanceMedia decoder = new AppearanceMediaDecoder(AppearanceMediaPath.Resolve(), AppearanceMediaPath.AnalysisHost());
    private readonly Channel<Request> requests = Channel.CreateBounded<Request>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? active;
    private string path = "";
    private double duration;
    private bool disposed;
    private long lastLevel;
    private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    public WindowAppearanceVisualBinding(Window window, ShellViewModel shell)
    {
        this.window = window; this.shell = shell; player = shell.Player;
        shell.FeaturePreferences.Changed += Options; window.PropertyChanged += WindowChanged;
        shell.PropertyChanged += ShellChanged; meterTimer.Tick += Meter;
        if (player is not null) player.PropertyChanged += PlayerChanged;
        Options(""); _ = Task.Run(Work);
    }
    private bool Enabled(string id) => shell.FeaturePreferences.Read(id, new FeatureSetting()).Enabled;
    private void Options(string id)
    {
        var model = shell.AppearanceVisuals; model.TopWaveform = Enabled("player-top-waveform"); model.EnergyProgress = Enabled("player-energy-progress");
        model.HomeWaveform = Enabled("home-waveform-visualizer"); model.WaveformScrollbar = Enabled("songs-waveform-scrollbar"); Refresh();
    }
    private void WindowChanged(object? s, AvaloniaPropertyChangedEventArgs e)
    { if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty) Refresh(); }
    private void ShellChanged(object? s, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ShellViewModel.ActiveRouteId)) UpdateMeter(); }
    private void UpdateMeter()
    {
        if (!disposed && shell.AppearanceVisuals.HomeWaveform && Enabled("audio-visual-spectrum") && shell.ActiveRouteId == "home" && window.IsVisible && window.WindowState != WindowState.Minimized)
        { meterTimer.Start(); Meter(null, EventArgs.Empty); }
        else { meterTimer.Stop(); shell.AppearanceVisuals.LiveSpectrum = []; }
    }
    private void Meter(object? sender, EventArgs args)
    {
        var state = player?.Zone.Snapshot.State; var level = state?.Level;
        if (state?.Playback.Status != PlaybackStatus.Playing || level?.VisualSpectrum is not { Count: > 0 } spectrum || Stopwatch.GetElapsedTime(level.Timestamp).TotalMilliseconds > 500)
        { if (shell.AppearanceVisuals.LiveSpectrum.Count > 0) shell.AppearanceVisuals.LiveSpectrum = []; shell.AppearanceVisuals.LiveStatus = "等待实时音频信号"; return; }
        if (lastLevel == level.Timestamp) return;
        lastLevel = level.Timestamp; shell.AppearanceVisuals.LiveSpectrum = spectrum; shell.AppearanceVisuals.LiveStatus = "";
    }
    private void PlayerChanged(object? s, PropertyChangedEventArgs e)
    { if (e.PropertyName is nameof(PlayerViewModel.CurrentFilePath) or nameof(PlayerViewModel.Duration)) Refresh(); }
    private void Refresh()
    {
        if (disposed) return;
        UpdateMeter();
        var model = shell.AppearanceVisuals; var wanted = model.TopWaveform || model.EnergyProgress;
        var next = wanted && window.IsVisible && window.WindowState != WindowState.Minimized ? player?.CurrentFilePath ?? "" : "";
        var nextDuration = player?.Duration ?? 0;
        if (next == path && nextDuration == duration) return;
        path = next; duration = nextDuration; active?.Cancel(); active?.Dispose(); active = null; model.Envelope = null; model.Status = "";
        if (next.Length == 0) return;
        if (!Path.IsPathFullyQualified(next) || player?.IsLiveStream == true) { model.Status = "当前来源没有可分析的本地音频，已保留普通进度条"; return; }
        active = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); model.Status = "正在分析当前曲目…";
        requests.Writer.TryWrite(new(next, player!.Duration, active.Token));
    }
    private async Task Work()
    {
        try
        {
            await foreach (var request in requests.Reader.ReadAllAsync(lifetime.Token))
            {
                if (request.Cancellation.IsCancellationRequested) continue;
                try
                {
                    var envelope = await decoder.AnalyzeAsync(request.Path, request.Duration, request.Cancellation);
                    await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed && !request.Cancellation.IsCancellationRequested && request.Path == path) { shell.AppearanceVisuals.Envelope = envelope; shell.AppearanceVisuals.Status = ""; } });
                }
                catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
                { await Dispatcher.UIThread.InvokeAsync(() => { if (!disposed && request.Path == path && !request.Cancellation.IsCancellationRequested) shell.AppearanceVisuals.Status = "当前曲目分析不可用，已保留普通进度条"; }); }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; lifetime.Cancel(); requests.Writer.TryComplete(); active?.Cancel(); active?.Dispose();
        shell.FeaturePreferences.Changed -= Options; window.PropertyChanged -= WindowChanged; if (player is not null) player.PropertyChanged -= PlayerChanged;
        meterTimer.Stop(); meterTimer.Tick -= Meter; shell.PropertyChanged -= ShellChanged; shell.AppearanceVisuals.LiveSpectrum = [];
        shell.AppearanceVisuals.Envelope = null;
    }
}
