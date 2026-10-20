using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Echo.Playback;

internal static class AlphaModelChecks
{
    internal static void Run()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(name => name.EndsWith("AlphaSourceReference.json")))!;
        using var reference = JsonDocument.Parse(stream);
        var model = new AutomixAlphaEqModel();
        var result = new double[12];
        var row = new float[36];
        foreach (var name in new[] { "zero", "ramp" })
        {
            for (var i = 0; i < 36; i++) row[i] = name == "zero" ? 0 : (float)((i - 18) / 30d);
            Assert.True(model.Forward(row, result), "source network accepts finite input");
            for (var i = 0; i < 12; i++) Assert.Near(result[i], reference.RootElement.GetProperty(name)[i].GetDouble(), 1e-12, name);
        }
        row[3] = float.NaN;
        Assert.True(!model.Forward(row, result), "invalid model input rejected");
        var a = new float[80 * 5]; var b = new float[80 * 5];
        for (var f = 0; f < 80; f++)
        {
            float[] av = [(float)(.25 + f % 10 * .01), (float)(.1 + f % 7 * .01), (float)(.08 + .002 * f), (float)(.05 + f % 3 * .005), 0];
            float[] bv = [(float)(.2 + f % 13 * .004), (float)(.12 + f % 5 * .008), (float)(.06 + .001 * f), (float)(.04 + f % 4 * .006), 0];
            av.CopyTo(a, f * 5); bv.CopyTo(b, f * 5);
        }
        var controls = AutomixAlphaEqFeatures.Controls(new(a, 80), new(b, 80), 1.6, 128, 145)!;
        for (var i = 0; i < 12; i++) Assert.Near(controls[i], reference.RootElement.GetProperty("controls")[i].GetDouble(), 1e-6, "feature/model controls");
        Assert.True(AutomixAlphaEqFeatures.Controls(new(a, 801), new(b, 801), 16.02, 128, 145) is null, "bounded model work");

        var samples = new float[11025 * 36];
        for (var i = 0; i < samples.Length; i++) samples[i] = (float)(.25 * Math.Sin(2 * Math.PI * 90 * i / 11025));
        _ = AutomixAlphaSpectral.Measure(samples, 11025); // Warm JIT outside measurement.
        var allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        var spectral = AutomixAlphaSpectral.Measure(samples, 11025)!;
        watch.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.True(spectral.Length == 9000 && spectral.All(value => float.IsFinite(value) && value >= 0), "fractional-rate full 36-second coverage");
        var last = spectral.Length - 5;
        Assert.True(spectral[last + 1] > spectral[last + 3] * 10, "90 Hz energy occupies low band");
        Assert.True(AutomixAlphaSpectral.Measure(samples, 7999) is null, "invalid analysis rate");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { _ = AutomixAlphaSpectral.Measure(samples, 11025, cancelled.Token); throw new Exception("expected cancellation"); }
        catch (OperationCanceledException) { }
        Console.WriteLine($"Alpha spectral incremental cost: {watch.Elapsed.TotalMilliseconds:F2} ms, {allocated} allocated bytes, {spectral.Length * sizeof(float)} retained bytes / 36 s mono 11025 Hz window (Release, warm JIT)");
    }
}
