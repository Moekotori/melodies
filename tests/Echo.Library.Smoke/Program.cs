using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
if (args[0] == "sort-filter")
{
    using var sortFilterBudget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await SortFilterProbe.RunAsync(Path.GetFullPath(args[1]), sortFilterBudget.Token); return;
}
if (args[0] == "large-library-final")
{
    using var finalBudget = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var finalRoot = Path.GetFullPath(args[1]); Directory.CreateDirectory(finalRoot);
    var finalScale = await ScaleProbe.RunAsync(Path.Combine(finalRoot, "scale"), finalBudget.Token, false, 5_000);
    Console.WriteLine($"FINAL_SCALE rows={finalScale.Records} insertMs={finalScale.InsertMs:F1} queryDuringWriteMaxMs={finalScale.QueryDuringWriteMaxMs:F1} pageP95Ms={finalScale.PageQueryP95Ms:F1} privateBytes={finalScale.ProcessPrivateBytes}");
    Console.WriteLine("LEGACY_CHECKS_PASSED=" + await LegacyMigrationProbe.RunAsync(Path.Combine(finalRoot, "legacy"), finalBudget.Token));
    return;
}
if (args[0] == "large-library")
{
    using var largeBudget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var largeRoot = Path.GetFullPath(args[1]); Directory.CreateDirectory(largeRoot);
    if (args.Length < 3 || args[2] != "--remaining")
    {
        await LargeLibraryFilesProbe.RunAsync(largeRoot, largeBudget.Token);
        LargeLibraryFilesProbe.CueFolder(largeRoot, largeBudget.Token);
    }
    await LargeLibraryRecoveryProbe.RunAsync(largeRoot, largeBudget.Token);
    await LargeLibraryMaintenanceProbe.RunAsync(largeRoot, largeBudget.Token);
    var scaleResult = await ScaleProbe.RunAsync(Path.Combine(largeRoot, "database-scale"), largeBudget.Token, includeAccuracy: false);
    Console.WriteLine($"DATABASE_SCALE rows={scaleResult.Records} insertMs={scaleResult.InsertMs:F1} queryDuringWriteMaxMs={scaleResult.QueryDuringWriteMaxMs:F1} pageP95Ms={scaleResult.PageQueryP95Ms:F1} privateBytes={scaleResult.ProcessPrivateBytes}");
    Console.WriteLine("LARGE_LIBRARY_PASSED");
    return;
}
if (args[0] == "legacy")
{
    using var legacyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    var checkedCount = await LegacyMigrationProbe.RunAsync(Path.GetFullPath(args[1]), legacyTimeout.Token);
    Console.WriteLine("LEGACY_CHECKS_PASSED=" + checkedCount);
    return;
}

if (args[0] == "selected-import")
{
    using var importBudget = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await SelectedImportProbe.RunAsync(Path.GetFullPath(args[1]), importBudget.Token); return;
}

if (args[0] == "v5-fixture") { MigrationFixture.CreateV5(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); return; }
if (args[0] == "history")
{
    using var historyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
    await HistoryProbe.RunAsync(Path.GetFullPath(args[1]), historyTimeout.Token); return;
}

if (args[0] == "browse")
{
    using var browseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await BrowseProbe.RunAsync(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]), browseTimeout.Token); return;
}

if (args[0] == "search")
{
    using var searchTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await SearchProbe.RunAsync(Path.GetFullPath(args[1]), searchTimeout.Token); return;
}

if (args[0] == "metadata")
{
    using var metadataTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    await MetadataProbe.RunAsync(Path.GetFullPath(args[1]), metadataTimeout.Token); return;
}

if (args[0] == "artists")
{
    using var artistTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    await ArtistProbe.RunAsync(Path.GetFullPath(args[1]), artistTimeout.Token); return;
}
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
