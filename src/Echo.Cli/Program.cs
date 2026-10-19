using Echo.Core.Client;
using Echo.Core.Transport;
using System.Text.Json;
using System.Text.Json.Serialization;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    if (args.Length == 0 || args[0] is "help" or "--help")
    {
        Console.WriteLine("scan --db FILE --root DIRECTORY [--native-scanner FILE] [--workers 2] [--batch 128] [--force] [--detect-missing]");
        Console.WriteLine("list --db FILE [--search TEXT] [--limit 100]\nstats --db FILE\nissues --db FILE --job ID [--limit 16]");
        Console.WriteLine("albums --db FILE [--search TEXT] [--limit 100]\nalbum-metadata --db FILE --id ID");
        Console.WriteLine("set-album-metadata --db FILE --id ID --field title|artist --source user|online --value TEXT\nclear-album-metadata --db FILE --id ID --field title|artist --source user|online");
        Console.WriteLine("远程 Control：用 --connection FILE 替代 --db；FILE 为 Core 所有者签发的配对文件。远程只开放浏览和播放控制能力。");
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
    if (command is not ("scan" or "list" or "stats" or "issues" or "albums" or "album-metadata" or "set-album-metadata" or "clear-album-metadata")) throw new ArgumentException("Unknown command.");
    var allowed = command switch
    {
        "scan" => new[] { "db", "root", "native-scanner", "workers", "batch", "force", "detect-missing" },
        "list" => ["db", "search", "limit"],
        "albums" => ["db", "search", "limit"],
        "album-metadata" => ["db", "id"],
        "set-album-metadata" => ["db", "id", "field", "source", "value"],
        "clear-album-metadata" => ["db", "id", "field", "source"],
        "issues" => ["db", "job", "limit"],
        _ => ["db"]
    };
    if (options.Keys.Any(key => !allowed.Append("connection").Contains(key))) throw new ArgumentException("Unknown option.");
    string Required(string key) => options.TryGetValue(key, out var value) ? value : throw new ArgumentException("Missing --" + key);
    int Number(string key, int fallback) => options.TryGetValue(key, out var value) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : fallback;
    if (options.ContainsKey("db") && options.ContainsKey("connection")) throw new ArgumentException("--db 与 --connection 不能同时使用。");
    var pairedPath = options.GetValueOrDefault("connection");
    var databasePath = options.TryGetValue("db", out var selectedDatabase) ? Path.GetFullPath(selectedDatabase) : null;
    await using var core = pairedPath is not null
        ? await CoreConnection.ConnectRemoteAsync(RemoteCoreProfile.Load(pairedPath), cancellation.Token)
        : await CoreLauncher.ConnectAsync(databasePath is null ? null : new CoreEndpoint(Path.GetDirectoryName(databasePath)!, databasePath),
            hostPath: ServiceExecutable.Resolve(Path.Combine(AppContext.BaseDirectory, "core"), "Echo.Core.Host", "ECHO_CORE_HOST"), token: cancellation.Token);
    var database = core.Library;
    if (command is "album-metadata" or "set-album-metadata" or "clear-album-metadata")
    {
        IAlbumMetadata metadata = database;
        if (command != "album-metadata")
        {
            var field = Enum.Parse<AlbumMetadataField>(Required("field"), true);
            var source = Enum.Parse<MetadataSource>(Required("source"), true);
            await metadata.SetAlbumMetadataAsync(Required("id"), field, source, command == "set-album-metadata" ? Required("value") : null, cancellation.Token);
        }
        Console.WriteLine(JsonSerializer.Serialize(await metadata.GetAlbumMetadataAsync(Required("id"), cancellation.Token), CliJson.Default.AlbumMetadataSnapshot));
        return 0;
    }
    if (command == "albums")
    {
        options.TryGetValue("search", out var search);
        Console.WriteLine(JsonSerializer.Serialize(await database.GetAlbumsAsync(new(Limit: Number("limit", 100), Search: search), cancellation.Token), CliJson.Default.AlbumPage));
        return 0;
    }
    if (command == "scan")
    {
        options.TryGetValue("native-scanner", out var native);
        ScanReport result;
        try
        {
            result = await database.ScanAsync(new(Required("root"), Number("workers", 2), Number("batch", 128),
                options.ContainsKey("force"), options.ContainsKey("detect-missing")), native, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            using var cancelLimit = new CancellationTokenSource(1000);
            try { await database.CancelActiveTaskAsync(cancelLimit.Token); } catch (Exception) { }
            throw;
        }
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
[JsonSerializable(typeof(AlbumMetadataSnapshot))]
[JsonSerializable(typeof(AlbumPage))]
[JsonSerializable(typeof(IReadOnlyList<ScanIssue>), TypeInfoPropertyName = "ScanIssues")]
internal partial class CliJson : JsonSerializerContext;
