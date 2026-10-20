using Avalonia.Controls;
using Avalonia.Media.Transformation;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Shell;

// Lyrics is a sheet over the shell: it rises from the bottom edge over the still page and drops
// back onto the destination. One lane; the page beneath is released only once it is covered,
// and the leaving lyrics view only once it has landed.
public partial class MainWindow
{
    private WindowState? beforeLyrics;
    private readonly MotionLane _lyricsMotion = new();
    private LyricsView? _leavingLyrics;
    private int _lyricsGeneration;
    private bool _lyricsLowering;

    private bool ShowStandaloneLyrics()
    {
        if (_viewModel?.ActiveRouteId == "lyrics" && _viewModel.LyricsSource is not null)
        {
            beforeLyrics ??= WindowState;
            _viewModel.CloseQueueDrawerCommand.Execute(null);
            _viewModel.Player.Queue.ShowHistory = false;
            ReleaseLyricsPage();
            _lyricsPage = new LyricsView(_viewModel, _previousRoute);
            LyricsStageHost.Content = _lyricsPage;
            _leavingLyrics?.Dispose(); _leavingLyrics = null;
            RaiseLyricsStage();
            return true;
        }
        if (LyricsStageHost.IsVisible && !_lyricsLowering) _ = LowerLyricsStage();
        return false;
    }

    private async void RaiseLyricsStage()
    {
        var generation = ++_lyricsGeneration;
        _lyricsLowering = false;
        var spatial = _viewModel?.Motion.UseSpatialMotion == true;
        var shown = LyricsStageHost.IsVisible;
        var opacity = shown ? MotionLane.CurrentOpacity(LyricsStageHost) : spatial ? 1 : 0;
        var pose = shown ? MotionLane.Current(LyricsStageHost) : SheetPose(spatial);
        LyricsStageHost.IsVisible = true; LyricsStageHost.IsHitTestVisible = true;
        UiScaleHost.IsHitTestVisible = false;
        await _lyricsMotion.PlayAsync(LyricsStageHost, opacity, 1, pose, MotionLane.At(),
            MotionTokens.SheetRise, MotionTokens.SheetEasing, preserveGeometry: true);
        if (generation != _lyricsGeneration || _surface is null) return;
        UiScaleHost.IsVisible = false; UiScaleHost.IsHitTestVisible = true;
        ReleaseLibraryPage(); PageHost.Content = null; PageHost.Settle();
    }

    // Completes once the drop runs on the compositor, so the destination can be built beneath it.
    private Task LowerLyricsStage()
    {
        if (!LyricsStageHost.IsVisible) return Task.CompletedTask;
        var generation = ++_lyricsGeneration;
        _lyricsLowering = true;
        _leavingLyrics?.Dispose(); _leavingLyrics = _lyricsPage; _lyricsPage = null;
        UiScaleHost.IsVisible = true; UiScaleHost.IsHitTestVisible = true;
        LyricsStageHost.IsHitTestVisible = false;
        if (LyricsStageHost.IsKeyboardFocusWithin) FocusManager?.Focus(null);
        if (beforeLyrics is { } previous) WindowState = previous;
        beforeLyrics = null;
        var spatial = _viewModel?.Motion.UseSpatialMotion == true;
        var drop = _lyricsMotion.PlayAsync(LyricsStageHost, MotionLane.CurrentOpacity(LyricsStageHost), spatial ? 1 : 0,
            MotionLane.Current(LyricsStageHost), SheetPose(spatial), MotionTokens.SheetDrop, MotionTokens.SheetDropEasing, preserveGeometry: true);
        _ = FinishLowering(generation, drop);
        return _lyricsMotion.Started;
    }
    private async Task FinishLowering(int generation, Task drop)
    {
        await drop;
        if (generation != _lyricsGeneration) return;
        _lyricsLowering = false;
        _leavingLyrics?.Dispose(); _leavingLyrics = null;
        LyricsStageHost.Content = null; LyricsStageHost.IsVisible = false; LyricsStageHost.IsHitTestVisible = true;
        LyricsStageHost.Opacity = 1; LyricsStageHost.RenderTransform = MotionLane.At();
    }
    // Reduced motion keeps the sheet in place and only fades it.
    private TransformOperations SheetPose(bool spatial) =>
        MotionLane.At(spatial ? Math.Max(LyricsStageHost.Bounds.Height, _surface?.Bounds.Height ?? 0) : 0);
    private void SettleLyricsStage()
    {
        ++_lyricsGeneration;
        _lyricsLowering = false;
        _lyricsMotion.Cancel();
        _leavingLyrics?.Dispose(); _leavingLyrics = null;
    }
}
