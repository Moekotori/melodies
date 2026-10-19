using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Controls.Lyrics;

public sealed class SourceLyricsSceneControl : Control, IDisposable
{
    private LyricsViewModel? model;
    private MotionPolicy? motion;
    private SourceSceneWorker? worker;
    private readonly LyricsFrameController frames;
    private SourceSceneFrame? frame;
    private SourceSceneReceipt? pending;
    private SourceSceneProject? project;
    private object? key;
    private Avalonia.Media.Imaging.Bitmap? cover;
    private byte[]? coverBytes;
    private SourceSceneArtwork? artwork;
    private bool artworkPending;
    private long revision, lastRequest;
    private int posted;
    private bool closed, failed, focused = true, visible;
    private int width, height;
    public bool HasFrame => frame is not null;
    public bool IsFrameDriverRunning => frames.IsRunning;
    public string RenderDiagnostic { get; private set; } = "";
    public event Action<bool>? FallbackChanged;
    public SourceLyricsSceneControl()
    {
        ClipToBounds = true; IsVisible = false;
        frames = new(RequestFrame, () => visible && !failed && motion?.CanAnimate == true, () => Math.Min(focused ? 30 : 15, motion?.MaximumFrameRate ?? 30));
        DetachedFromVisualTree += (_, _) => Dispose();
        SizeChanged += (_, _) => Changed();
    }
    public void Bind(LyricsViewModel value, MotionPolicy policy)
    {
        model = value; motion = policy;
        model.PropertyChanged += ModelPropertyChanged;
        model.Changed += Changed; model.Appearance.PropertyChanged += AppearanceChanged;
        motion.PropertyChanged += AppearanceChanged; Changed();
    }
    private void ModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(LyricsViewModel.Cover)) Changed(); }
    private void AppearanceChanged(object? sender, PropertyChangedEventArgs e) => Changed();
    private void Changed()
    {
        if (closed || model is null || motion is null) return;
        var enabled = model.Appearance.Enabled && model.Appearance.SelectedStyle.Layout == LyricsLayout.SourceScene
            && model.Document.IsSynced && model.Document.Lines.Count > 0;
        visible = enabled && motion.IsWindowVisible;
        IsVisible = enabled && !failed;
        if (!visible)
        {
            frames.Update(false, false);
            if (key is not null || frame is not null)
            {
                revision++; key = null; project = null; cover = null; coverBytes = null; artworkPending = false;
                artwork?.Show(null);
                worker?.Request(new(revision, "", null, 0, 0, 0, false, false));
                frame?.Dispose(); frame = null; Interlocked.Exchange(ref pending, null)?.Frame?.Dispose();
            }
            return;
        }
        if (Bounds.Width < 2 || Bounds.Height < 2) return;
        var scale = Math.Min(1, Math.Min(8192 / Bounds.Width, 8192 / Bounds.Height));
        width = Math.Max(1, (int)(Bounds.Width * scale)); height = Math.Max(1, (int)(Bounds.Height * scale));
        var a = model.Appearance;
        var sceneCover = a.SelectedStyle.Id == "cassette" ? model.Cover : null;
        var nextKey = (model.Document, a.SelectedStyle.Id, model.Title, model.Artist, width, height,
            a.Translation, a.Font, a.FontSize, a.SecondarySize, sceneCover);
        if (!Equals(key, nextKey))
        {
            key = nextKey; revision++; failed = false; IsVisible = enabled; FallbackChanged?.Invoke(false); RenderDiagnostic = "";
            if (!ReferenceEquals(cover, sceneCover))
            {
                cover = sceneCover; coverBytes = null; artworkPending = cover is not null;
                if (artworkPending)
                {
                    artwork ??= new(ArtworkReady);
                    var lease = model.RetainCover();
                    if (lease is null) artworkPending = false;
                    artwork.Show(lease);
                }
                else artwork?.Show(null);
            }
            project = SourceSceneProjectBuilder.Create(model.Document, model.Title, model.Artist,
                model.Player.Duration * 1000, a.Translation, a.Font, a.FontSize, a.SecondarySize) with { Artwork = coverBytes };
            frame?.Dispose(); frame = null;
        }
        RequestFrame(true); frames.Update(true, focused);
    }
    private void ArtworkReady(byte[]? bytes)
    {
        coverBytes = bytes; artworkPending = false;
        if (project is not null) project = project with { Artwork = bytes };
        RequestFrame(true);
    }
    private void RequestFrame() => RequestFrame(false);
    private void RequestFrame(bool force)
    {
        if (!visible || failed || closed || artworkPending || model is null || project is null || motion is null) return;
        if (!force && frame is not null && Stopwatch.GetElapsedTime(lastRequest).TotalMilliseconds < 1000d / (focused ? 30 : 15)) return;
        lastRequest = Stopwatch.GetTimestamp(); worker ??= new(Receive);
        worker.Request(new(revision, model.Appearance.SelectedStyle.Id, project, width, height,
            model.PositionMs(motion.CanAnimate) + model.Document.OffsetMs, motion.UseSpatialMotion, model.IsPlaying));
    }
    private void Receive(SourceSceneReceipt value)
    {
        if (value.SeekMs is { } seek)
            Dispatcher.UIThread.Post(() => { if (!closed && value.Revision == revision && model is not null) _ = model.Player.SeekToAsync(Math.Max(0, (seek - model.Document.OffsetMs - model.Appearance.OffsetMs) / 1000)); });
        Interlocked.Exchange(ref pending, value)?.Frame?.Dispose();
        if (Interlocked.Exchange(ref posted, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref posted, 0); var receipt = Interlocked.Exchange(ref pending, null);
            if (receipt is null) return;
            if (closed || !visible || receipt.Revision != revision) { receipt.Frame?.Dispose(); return; }
            frame?.Dispose(); frame = receipt.Frame; RenderDiagnostic = receipt.Diagnostic ?? "";
            if (receipt.Error is not null) { failed = true; frames.Update(false, false); IsVisible = false; FallbackChanged?.Invoke(true); }
            InvalidateVisual();
        });
    }
    public override void Render(DrawingContext context)
    {
        if (frame is not null) context.Custom(new SourceSceneDraw(frame, new Rect(Bounds.Size)));
        else context.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
    }
    public void SetFocused(bool value) { focused = value; frames.Update(visible, value); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // The original scene hit boxes are processed on its owner worker; they return only a lyric time.
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { var p = e.GetPosition(this); RequestPointer(p, true); e.Handled = true; }
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (focused) RequestPointer(e.GetPosition(this), false); }
    private void RequestPointer(Point point, bool press)
    {
        if (!visible || failed || artworkPending || project is null || model is null || motion is null) return;
        if (!press && Stopwatch.GetElapsedTime(lastRequest).TotalMilliseconds < 33) return;
        lastRequest = Stopwatch.GetTimestamp(); worker ??= new(Receive);
        worker.Request(new(revision, model.Appearance.SelectedStyle.Id, project, width, height,
            model.PositionMs(motion.CanAnimate) + model.Document.OffsetMs, motion.UseSpatialMotion, model.IsPlaying,
            point.X * width / Bounds.Width, point.Y * height / Bounds.Height, press));
    }
    public void Dispose()
    {
        if (closed) return; closed = true; revision++; frames.Dispose(); worker?.Dispose(); artwork?.Dispose();
        frame?.Dispose(); frame = null; Interlocked.Exchange(ref pending, null)?.Frame?.Dispose();
        if (model is not null) { model.PropertyChanged -= ModelPropertyChanged; model.Changed -= Changed; model.Appearance.PropertyChanged -= AppearanceChanged; }
        if (motion is not null) motion.PropertyChanged -= AppearanceChanged;
        cover = null; coverBytes = null; model = null; motion = null; project = null; key = null; FallbackChanged = null;
    }
}
