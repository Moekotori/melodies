using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

if (args[0] == "albums-query")
{
    using var readTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await AlbumProbe.ReadAsync(Path.GetFullPath(args[1]), readTimeout.Token); return;
}
if (args[0] is "albums" or "albums-check")
{
    var albumRoot = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(albumRoot);
    using var albumTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    if (args[0] == "albums") await AlbumProbe.RunAsync(albumRoot, albumTimeout.Token);
    else await AlbumMaintenanceProbe.RunAsync(albumRoot, albumTimeout.Token);
    return;
}
var scale = args[0] == "scale";
var accuracy = args[0] == "accuracy";
var root = Path.GetFullPath(args[scale || accuracy ? 1 : 0]);
Directory.CreateDirectory(root);
if (accuracy)
{
    using var accuracyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var accuracyWatch = Stopwatch.StartNew();
    var checkedCount = await AccuracyProbe.RunAsync(root, accuracyTimeout.Token);
    var report = new AccuracyResult(checkedCount, accuracyWatch.Elapsed.TotalMilliseconds);
    var output = JsonSerializer.Serialize(report, SmokeJson.Default.AccuracyResult);
    await File.WriteAllTextAsync(Path.Combine(root, "results.json"), output, accuracyTimeout.Token);
    Console.WriteLine(output);
    return;
}
if (scale)
{
    using var scaleTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    var report = await ScaleProbe.RunAsync(root, scaleTimeout.Token);
    var output = JsonSerializer.Serialize(report, SmokeJson.Default.ScaleResult);
    await File.WriteAllTextAsync(Path.Combine(root, "results.json"), output, scaleTimeout.Token);
    Console.WriteLine(output);
    return;
}
var native = args.Length > 1 ? Path.GetFullPath(args[1]) : null;
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
var watch = Stopwatch.StartNew();
var checks = await Scenarios.RunAsync(root, native, timeout.Token);
var performance = await PerformanceProbe.RunAsync(root, native, timeout.Token);
var result = new SmokeReport(checks, watch.Elapsed.TotalMilliseconds, performance);
var json = JsonSerializer.Serialize(result, SmokeJson.Default.SmokeReport);
await File.WriteAllTextAsync(Path.Combine(root, "results.json"), json, timeout.Token);
Console.WriteLine(json);
internal sealed record SmokeReport(int ChecksPassed, double ElapsedMs, ProbeResult Performance);
internal sealed record AccuracyResult(int ChecksPassed, double ElapsedMs);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(SmokeReport))]
[JsonSerializable(typeof(ScaleResult))]
[JsonSerializable(typeof(AccuracyResult))]
internal partial class SmokeJson : JsonSerializerContext;
