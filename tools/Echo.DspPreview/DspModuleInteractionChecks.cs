using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Echo.Desktop.Features.Dsp;

internal static class DspModuleInteractionChecks
{
    // Real controls and native replies: catches detached fields, dropped nested booleans,
    // stuck hold-to-listen, and scene saves accidentally ignoring an unapplied rack draft.
    public static void Run(DspView view, DspViewModel model, Window window, Action wait, Action settle, Action<Func<bool>> until)
    {
        settle(); // A freshly selected, lazily created workbench needs its first layout before pointer input.
        var id = model.SelectedModule.Id;
        foreach (var editor in view.GetVisualDescendants().OfType<DspParameterEditor>())
            if (editor.DataContext is not DspParameter) throw new IOException(id + ": unbound parameter editor");
        foreach (var editor in view.GetVisualDescendants().OfType<DspChoiceEditor>())
            if (editor.DataContext is not DspChoiceField) throw new IOException(id + ": unbound choice editor");
        foreach (var editor in view.GetVisualDescendants().OfType<DspToggleEditor>())
            if (editor.DataContext is not DspToggle) throw new IOException(id + ": unbound switch editor");
        foreach (var editor in view.GetVisualDescendants().OfType<DspChoiceSegments>())
            if (editor.DataContext is not DspChoiceField) throw new IOException(id + ": unbound segmented choice");

        if (id == "deesser")
        {
            var plot = view.GetVisualDescendants().OfType<DspDiagramControl>().Single(x => x.Kind == "deesserBand");
            var before = model.Fields["frequencyHz"].Value;
            var point = plot.TranslatePoint(new Point(plot.Bounds.Width * .84, plot.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); settle();
            var expected = model.Fields["frequencyHz"].Value;
            if (Math.Abs(expected - before) < 1 || !model.IsDirty) throw new IOException("Detection-band drag did not change its frequency");
            model.ApplyCommand.Execute(null); wait(); model.RefreshCommand.Execute(null); wait();
            if (Math.Abs(model.Fields["frequencyHz"].Value - expected) > 1) throw new IOException("Detection-band frequency was lost after native reread");
        }

        if (id is "headroom" or "compressor" or "deesser" or "limiter" or "crossfeed" or "virtualStage" or "stereoField" or "channelMatrix" or "channel")
        {
            if (id == "limiter") { model.Switches["limiterAutoCeiling"].Value = false; settle(); }
            var editor = view.GetVisualDescendants().OfType<DspParameterEditor>().First(x => x.DataContext is DspParameter p && p.Value + p.Step <= p.Maximum);
            var parameter = (DspParameter)editor.DataContext!;
            var key = parameter.Definition.Key; var before = parameter.Value;
            var input = editor.FindControl<NumericUpDown>("ParameterValue")!;
            input.Focus(); window.KeyPress(Key.Up, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            window.KeyRelease(Key.Up, Avalonia.Input.RawInputModifiers.None, PhysicalKey.ArrowUp, null); settle();
            if (parameter.Value <= before || !model.IsDirty) throw new IOException(id + ": keyboard edit did not reach the draft");
            var expected = parameter.Value;
            model.ApplyCommand.Execute(null); wait();
            if (model.IsDirty) throw new IOException(id + ": " + model.Message);
            model.RefreshCommand.Execute(null); wait();
            if (Math.Abs(model.Fields[key].Value - expected) > parameter.Step * .51) throw new IOException(id + ": native reread lost the parameter");
            if (id == "limiter" && !view.GetVisualDescendants().OfType<DspParameterEditor>().First().FindControl<NumericUpDown>("ParameterValue")!.IsEffectivelyEnabled)
                throw new IOException("Manual limiter ceiling remained disabled after native reread");
        }
        if (id == "multiband")
        {
            var plot = view.GetVisualDescendants().OfType<DspDiagramControl>().Single(x => x.Kind == "multibandBands");
            var point = plot.TranslatePoint(new Point(plot.Bounds.Width * .94, plot.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); settle();
            if (model.CompressionBand != model.CompressionBands[2]) throw new IOException("Clicking a crossover region did not select the band");
            model.CompressionBand = model.CompressionBands[1]; settle();
            var control = view.GetVisualDescendants().OfType<DspToggleEditor>().Single().GetVisualDescendants().OfType<ToggleSwitch>().Single();
            var expected = !model.Switches["bands.1.enabled"].Value;
            control.IsChecked = expected; settle(); model.ApplyCommand.Execute(null); wait();
            model.RefreshCommand.Execute(null); wait();
            if (model.Switches["bands.1.enabled"].Value != expected) throw new IOException("Native multiband band-enable was not retained");
        }
        if (id == "headroom")
        {
            var button = view.GetVisualDescendants().OfType<Button>().Single(x => x.Name == "HeadroomListenButton");
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); until(() => model.HeadroomListening);
            window.MouseUp(point, MouseButton.Left); until(() => !model.HeadroomListening);
        }
        if (id == "listening")
        {
            var listening = view.GetVisualDescendants().OfType<DspListeningView>().Single();
            var rack = (DspViewModel)listening.FindControl<StackPanel>("ChainHost")!.DataContext!;
            until(() => !rack.IsBusy);
            rack.AddRackFilterCommand.Execute(null); settle();
            if (!model.ListeningChainPending || model.CanSaveListening) throw new IOException("Scene save ignored the unapplied rack draft");
            rack.ApplyCommand.Execute(null); until(() => !rack.IsBusy);
            if (rack.IsDirty || model.ListeningChainPending) throw new IOException("Nested rack did not confirm the native apply: " + rack.Message);
            model.ListeningName = "DSP UI check"; model.SaveListeningCommand.Execute(null); wait();
            model.SelectedProfile = model.Profiles.Single(x => x.Name == "DSP UI check");
            model.DeleteListeningCommand.Execute(null); wait();
            if (model.Profiles.Any(x => x.Name == "DSP UI check")) throw new IOException("Scene deletion was not confirmed by Core");
        }
    }
}
