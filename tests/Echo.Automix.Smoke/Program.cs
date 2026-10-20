using System.Diagnostics;
using System.Text.Json;
using Echo.Playback;

var watch = Stopwatch.StartNew();
AutomixPlannerChecks.Run();
AlphaModelChecks.Run();
AlphaPlanningChecks.Run();
await AlphaProtocolChecks.RunAsync();
Console.WriteLine($"PASS Alpha model/source parity, spectral/material bounds, six routes, cue planning and host receipts ({watch.ElapsedMilliseconds} ms)");

internal static class Assert
{
    internal static void True(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    internal static void Near(double actual, double expected, double tolerance, string message) =>
        True(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"{message}: {actual} != {expected}");
}
