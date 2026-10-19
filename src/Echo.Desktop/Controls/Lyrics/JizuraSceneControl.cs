using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Echo.Contracts.Lyrics;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Motion;
using Echo.Visuals;

namespace Echo.Desktop.Controls.Lyrics;

public sealed partial class JizuraSceneControl : Control, IDisposable
{
    private LyricsViewModel? model;
    private MotionPolicy? motion;
    private JizuraSceneWorker? worker;
    private LyricsDocument? document;
    private RasterImageFrame? frame;
    private string look = "", title = "", error = "";
    private bool words, closed, active;
    private long version, lastRequest;
    private JizuraReceipt? pending;
    private int posted;
    public bool HasFrame => frame is not null;
    public string RenderError => error;
    public string RenderDiagnostic { get; private set; } = "";
    public JizuraSceneControl()
    {
        IsHitTestVisible = false; ClipToBounds = true; IsVisible = false;
        retirement.Tick += RetireScene;
    }
    public void Bind(LyricsViewModel value, MotionPolicy policy)
    {
        model = value; motion = policy; model.Changed += Changed;
        model.Appearance.PropertyChanged += AppearanceChanged; motion.PropertyChanged += AppearanceChanged;
        DetachedFromVisualTree += (_, _) => Dispose(); Changed();
    }
    private void AppearanceChanged(object? sender, PropertyChangedEventArgs e) => Changed();
    private void Changed()
    {
        if (closed || model is null) return;
        var enabled = model.Appearance.IsAfterglow && model.Document.Lines.Count > 0;
        var changed = enabled != active || !ReferenceEquals(document, model.Document) || look != model.Appearance.SelectedLook.Id
            || title != model.Title || words != model.Appearance.WordHighlight;
        active = enabled; IsVisible = enabled;
        if (changed)
        {
            version++; document = model.Document; look = model.Appearance.SelectedLook.Id; title = model.Title;
            words = model.Appearance.WordHighlight; error = ""; frame?.Dispose(); frame = null;
            if (!active) { ReleaseScene(); InvalidateVisual(); return; }
        }
        var restore = !resourcesResident || retirement.IsEnabled;
        if (!UpdateResidency()) return;
        UpdateClock(model.PositionMs(false), motion?.UseSpatialMotion == true && model.IsPlaying, changed || restore || !model.IsPlaying);
    }
    public void UpdateClock(double positionMs, bool animate) => UpdateClock(positionMs, animate, false);
    private void UpdateClock(double positionMs, bool animate, bool force)
    {
        if (!active || closed || model is null || motion?.IsWindowVisible != true) return;
        // At most 12 requests/sec; an expensive frame replaces the pending request instead of building a backlog.
        if (!force && Stopwatch.GetElapsedTime(lastRequest).TotalMilliseconds < 83) return;
        lastRequest = Stopwatch.GetTimestamp(); worker ??= new(Receive); resourcesResident = true;
        var seconds = Math.Max(0, (positionMs + model.Document.OffsetMs) / 1000);
        worker.Request(new(version, model.Document, JizuraProjectBuilder.Window(model.Document, positionMs / 1000),
            model.Title, model.Artist, look, seconds, animate, words));
    }
    private void Receive(JizuraReceipt receipt)
    {
        if (Volatile.Read(ref closed)) { receipt.Frame?.Dispose(); return; }
        var replaced = Interlocked.Exchange(ref pending, receipt); replaced?.Frame?.Dispose();
        if (Volatile.Read(ref closed)) { Interlocked.Exchange(ref pending, null)?.Frame?.Dispose(); return; }
        if (Interlocked.Exchange(ref posted, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref posted, 0); var result = Interlocked.Exchange(ref pending, null);
            if (result is null) return;
            if (closed || !active || motion?.IsWindowVisible != true || result.Version != version) { result.Frame?.Dispose(); return; }
            frame?.Dispose(); frame = result.Frame; error = result.Error ?? ""; RenderDiagnostic = result.Diagnostic ?? ""; InvalidateVisual();
        });
    }
    public override void Render(DrawingContext context)
    {
        if (!active || closed || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        context.FillRectangle(new SolidColorBrush(Color.Parse(model!.Appearance.SelectedLook.Background)), new Rect(Bounds.Size));
        if (frame is not null)
        {
            var scale = Math.Min(Bounds.Width / frame.Width, Bounds.Height / frame.Height);
            var size = new Size(frame.Width * scale, frame.Height * scale);
            context.Custom(new JizuraFrameDraw(frame, new Rect((Bounds.Width-size.Width)/2, (Bounds.Height-size.Height)/2, size.Width, size.Height)));
        }
        else LyricsStagePainter.Label(context, model.Player.Text.Translate(error.Length > 0 ? error : "正在准备 Afterglow 场景…"), 36, Bounds.Height*.5, 20,
            new SolidColorBrush(Color.Parse(model.Appearance.SelectedLook.Ink)));
    }
    public void Dispose()
    {
        if (closed) return; closed = true; version++;
        retirement.Stop(); retirement.Tick -= RetireScene;
        if (model is not null) { model.Changed -= Changed; model.Appearance.PropertyChanged -= AppearanceChanged; }
        if (motion is not null) motion.PropertyChanged -= AppearanceChanged;
        worker?.Dispose(); frame?.Dispose(); frame = null; Interlocked.Exchange(ref pending, null)?.Frame?.Dispose();
        model = null; motion = null;
    }
}
