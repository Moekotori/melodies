using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (args.Length == 0 || args[0] is "help" or "--help")
    {
        Console.WriteLine("scan --db FILE --root DIRECTORY [--native-scanner FILE] [--workers 2] [--batch 128] [--force] [--detect-missing]");
        Console.WriteLine("list --db FILE [--search TEXT] [--limit 100]\nstats --db FILE\nissues --db FILE --job ID [--limit 16]");
        return 0;
    }
    var command = args[0];
    var options = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var i = 1; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Expected an option.");
        var key = args[i][2..];
        if (!options.TryAdd(key, key is "force" or "detect-missing" ? "true" :
            ++i < args.Length ? args[i] : throw new ArgumentException("Option requires a value.")))
            throw new ArgumentException("Duplicate option.");
    }
    if (command is not ("scan" or "list" or "stats" or "issues")) throw new ArgumentException("Unknown command.");
    var allowed = command switch
    {
        "scan" => new[] { "db", "root", "native-scanner", "workers", "batch", "force", "detect-missing" },
        "list" => ["db", "search", "limit"],
        "issues" => ["db", "job", "limit"],
        _ => ["db"]
    };
    if (options.Keys.Any(key => !allowed.Contains(key))) throw new ArgumentException("Unknown option.");
    string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing --" + key);
    int Number(string key, int fallback) => options.TryGetValue(key, out var value) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
    await using var database = await LibraryDatabase.OpenAsync(Required("db"), cancellation.Token);
    if (command == "scan")
    {
        options.TryGetValue("native-scanner", out var native);
        var scanner = new ScanCoordinator(database, new DirectoryDiscovery(), () => new LocalMetadataReader(native));
        var result = await scanner.ScanAsync(new(Required("root"), Number("workers", 2), Number("batch", 128),
            options.ContainsKey("force"), options.ContainsKey("detect-missing")), cancellation: cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(result, CliJson.Default.ScanReport));
        return result.Status == ScanStatus.Cancelled ? 130 : result.Status != ScanStatus.Completed || result.Failed != 0 ? 2 : 0;
    }
    if (command == "issues")
    {
        var issues = await database.GetScanIssuesAsync(Required("job"), Number("limit", 16), cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(issues, CliJson.Default.ScanIssues));
    }
    else if (command == "list")
    {
        options.TryGetValue("search", out var search);
        var page = await database.GetTracksAsync(new(Number("limit", 100), search), cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(page, CliJson.Default.TrackPage));
    }
    else Console.WriteLine(JsonSerializer.Serialize(await database.GetStatsAsync(cancellation.Token), CliJson.Default.LibraryStats));
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled."); return 130; }
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ScanReport))]
[JsonSerializable(typeof(TrackPage))]
[JsonSerializable(typeof(LibraryStats))]
[JsonSerializable(typeof(IReadOnlyList<ScanIssue>), TypeInfoPropertyName = "ScanIssues")]
internal partial class CliJson : JsonSerializerContext;
