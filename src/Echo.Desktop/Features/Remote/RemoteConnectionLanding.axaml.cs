using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Features.Remote;

// Seven retained provider tiles; layout changes animate on the compositor, without a frame timer.
public partial class RemoteConnectionLanding : UserControl
{
    private readonly Dictionary<Button, MotionLane> tileMotion = [];
    private readonly MotionLane formMotion = new();
    private RemoteViewModel? model;
    private MotionPolicy? policy;
    private bool queued;
    private bool switchForm;
    private bool attached;
    private (bool Open, bool Editing, bool Narrow, bool Small)? layout;
    public bool IsTransitioning => formMotion.IsRunning || tileMotion.Count > 0;

    public RemoteConnectionLanding()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => { attached = true; Observe(); QueueLayout(); };
        DetachedFromVisualTree += (_, _) => { attached = false; Unobserve(); Settle(); layout = null; };
        DataContextChanged += (_, _) => { if (attached) { Observe(); QueueLayout(); } };
        SizeChanged += (_, _) => QueueLayout();
    }
    public void FocusCurrent()
    {
        if (model?.EditorOpen == true) ConnectionEditor.FocusAddress(); else ProviderPicker.FocusFirst();
    }
    private void Observe()
    {
        Unobserve();
        model = DataContext as RemoteViewModel;
        if (model is not null) model.PropertyChanged += OnModelChanged;
        policy = MotionScope.GetPolicy(this);
        if (policy is not null) policy.PropertyChanged += OnPolicyChanged;
    }
    private void Unobserve()
    {
        if (model is not null) model.PropertyChanged -= OnModelChanged;
        if (policy is not null) policy.PropertyChanged -= OnPolicyChanged;
        model = null; policy = null;
    }
    private void OnPolicyChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(MotionPolicy.CanAnimate)) Settle(); }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(RemoteViewModel.DraftProvider)) switchForm = true;
        if (args.PropertyName is nameof(RemoteViewModel.EditorOpen) or nameof(RemoteViewModel.IsEditing)
            or nameof(RemoteViewModel.DraftProvider) or nameof(RemoteViewModel.ProviderChoices)) QueueLayout();
    }
    private void QueueLayout()
    {
        if (!attached || queued) return;
        queued = true;
        Dispatcher.UIThread.Post(() =>
        {
            queued = false;
            if (!attached || model is null) return;
            ApplyLayout();
        }, DispatcherPriority.Loaded);
    }
    private void ApplyLayout()
    {
        if (model is null) return;
        var next = (Open: model.EditorOpen, Editing: model.IsEditing, Narrow: Bounds.Width < 840, Small: Bounds.Width < 520);
        var tiles = ProviderPicker.GetVisualDescendants().OfType<Button>().Where(button => button.Tag is string).ToArray();
        var changed = layout != next;
        var animate = layout is not null && changed && IsEffectivelyVisible && policy?.UseSpatialMotion == true;
        var old = animate ? tiles.ToDictionary(button => button, Position) : null;
        if (changed) SettleTiles();
        foreach (var button in tiles) button.Classes.Set("active", next.Open && (string?)button.Tag == model.DraftProvider);
        if (changed)
        {
            Heading.IsVisible = !next.Open;
            ProviderPicker.IsVisible = !next.Editing;
            ProviderPicker.Classes.Set("rail", next.Open && !next.Narrow);
            ProviderPicker.Classes.Set("compact", next.Narrow);
            foreach (var grid in ProviderPicker.GetVisualDescendants().OfType<UniformGrid>())
                grid.Columns = next.Small || next.Open && !next.Narrow ? 1 : 2;
            var sideBySide = next.Open && !next.Editing && !next.Narrow;
            ConnectionBody.ColumnDefinitions[0].Width = sideBySide ? new(270) : new(1, GridUnitType.Star);
            ConnectionBody.ColumnDefinitions[1].Width = new(sideBySide ? 40 : 0);
            ConnectionBody.ColumnDefinitions[2].Width = sideBySide ? new(1, GridUnitType.Star) : new(0);
            Grid.SetColumnSpan(ProviderPicker, sideBySide ? 1 : 3);
            Grid.SetColumn(EditorSurface, sideBySide ? 2 : 0);
            Grid.SetColumnSpan(EditorSurface, sideBySide ? 1 : 3);
            Grid.SetRow(EditorSurface, next.Narrow && !next.Editing ? 1 : 0);
            EditorSurface.Margin = new Thickness(0, next.Narrow && !next.Editing ? 24 : 0, 0, 0);
            LandingBody.Margin = new Thickness(next.Small ? 0 : 24, next.Open ? 12 : 36, next.Small ? 0 : 24, 24);
            layout = next;
            UpdateLayout();
        }
        if (old is not null)
            foreach (var button in tiles)
            {
                var delta = old[button] - Position(button);
                MoveTile(button, delta);
            }
        if (switchForm && next.Open && !changed && policy?.CanAnimate == true && IsEffectivelyVisible)
        {
            var opacity = formMotion.IsRunning ? MotionLane.CurrentOpacity(ConnectionEditor) : 0;
            var pose = formMotion.IsRunning ? MotionLane.Current(ConnectionEditor) : MotionLane.At(x: policy.UseSpatialMotion ? 16 : 0);
            _ = formMotion.PlayAsync(ConnectionEditor, opacity, 1, pose, MotionLane.At(), MotionTokens.Reveal);
        }
        if (switchForm && next.Open) ConnectionEditor.FocusAddress();
        switchForm = false;
    }
    private async void MoveTile(Button button, Vector delta)
    {
        var lane = new MotionLane(); tileMotion.Add(button, lane);
        try
        {
            await lane.PlayAsync(button, 1, 1, MotionLane.At(delta.Y, x: delta.X), MotionLane.At(),
                MotionTokens.Drawer, preserveGeometry: true);
        }
        finally
        {
            if (tileMotion.TryGetValue(button, out var owner) && ReferenceEquals(owner, lane))
            { tileMotion.Remove(button); button.ClearValue(OpacityProperty); button.ClearValue(RenderTransformProperty); }
            lane.Dispose();
        }
    }
    private Point Position(Button button)
    {
        var point = button.TranslatePoint(default, ConnectionBody) ?? default;
        var live = MotionLane.Current(button).Value;
        var current = button.RenderTransform?.Value ?? Matrix.Identity;
        return point + new Vector(live.M31 - current.M31, live.M32 - current.M32);
    }
    private void SettleTiles()
    {
        var owners = tileMotion.ToArray();
        tileMotion.Clear();
        foreach (var (button, lane) in owners)
        { lane.Dispose(); button.ClearValue(OpacityProperty); button.ClearValue(RenderTransformProperty); }
    }
    private void Settle()
    {
        SettleTiles(); formMotion.Cancel();
        ConnectionEditor.Opacity = 1; ConnectionEditor.RenderTransform = MotionLane.At();
    }
}
