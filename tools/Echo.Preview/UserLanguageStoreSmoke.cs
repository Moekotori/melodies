using System.Diagnostics;
using System.Text;
using Echo.Desktop.Localization;

internal static class UserLanguageStoreSmoke
{
    public static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        var root = Path.Combine(output, "store-" + Guid.NewGuid().ToString("N"));
        var store = new UserLanguageStore(root); var baseline = LanguageCatalog.Default;
        Check((await store.ReadAsync(CancellationToken.None).ConfigureAwait(false)).Files.Count == 0, "Missing directory did not fall back.");
        Directory.CreateDirectory(root);
        var sourceKey = baseline.SourceKeys["设置"];
        File.WriteAllText(Path.Combine(root, "english.json"), Pack("en-US", "My English", "navigation.home", "My home"));
        File.WriteAllText(Path.Combine(root, "french.json"), Pack("fr-FR", "Français", "navigation.home", "Accueil"));
        File.WriteAllText(Path.Combine(root, "chinese.json"), Pack("zh-CN", "My Chinese", sourceKey, "Custom settings"));
        File.WriteAllText(Path.Combine(root, "broken.json"), "{invalid json");
        File.WriteAllText(Path.Combine(root, "arguments.json"), Pack("ja-JP", "日本語", "shell.pending", "{2}"));
        File.WriteAllText(Path.Combine(root, "unknown.json"), Pack("ko-KR", "한국어", "unknown.key", "x"));
        File.WriteAllBytes(Path.Combine(root, "oversized.json"), new byte[256 * 1024 + 1]);
        var snapshot = await store.ReadAsync(CancellationToken.None).ConfigureAwait(false);
        Check(snapshot.Files.Count == 3 && snapshot.Issues.Count == 4, "Invalid packages were not isolated.");
        var text = new LocalizationService(snapshot.Catalog); text.SetLanguage("en-US");
        Check(text["navigation.home"] == "My home" && text["navigation.albums"] == baseline.Packs["en-US"].Strings["navigation.albums"], "Built-in override fallback failed.");
        text.SetLanguage("zh-CN"); Check(text.Translate("设置") == "Custom settings", "Chinese override broke the source index.");
        text.SetLanguage("fr-FR"); Check(text["navigation.home"] == "Accueil" && text["navigation.albums"] == baseline.Fallback.Strings["navigation.albums"], "New language fallback failed.");
        var beforeFiles = Directory.GetFiles(root).Length;
        using (var invalidSpecifier = Stream(Pack("en-US", "Invalid specifier", "queue.count", "{0:y}")))
        {
            var safeText = new LocalizationService(baseline.WithUserPacks([LanguageCatalog.ReadPack(invalidSpecifier)])); safeText.SetLanguage("en-US");
            Check(safeText.Format("queue.count", 2) == "2 tracks" && safeText.Format("queue.count", 2) == "2 tracks", "Runtime formatter failure did not fall back safely.");
        }
        await ExpectFailureAsync(async () => { using var invalid = Stream(Pack("en-US", "Invalid", "unknown.key", "x")); await store.ImportAsync(invalid, CancellationToken.None).ConfigureAwait(false); }).ConfigureAwait(false);
        Check(Directory.GetFiles(root).Length == beforeFiles, "Invalid import wrote a file.");
        await ExpectFailureAsync(async () => { using var invalid = Stream(Pack("en-US", "Invalid", "shell.pending", "{0,2147483647}")); await store.ImportAsync(invalid, CancellationToken.None).ConfigureAwait(false); }).ConfigureAwait(false);
        await ExpectFailureAsync(async () => { using var invalid = Stream(Pack("en-US", "Invalid", "queue.count", "{0:N99999999}")); await store.ImportAsync(invalid, CancellationToken.None).ConfigureAwait(false); }).ConfigureAwait(false);
        using (var replacement = Stream(Pack("en-US", "My English", "navigation.home", "Updated home")))
        {
            var imported = await store.ImportAsync(replacement, CancellationToken.None).ConfigureAwait(false);
            Check(imported.Snapshot.Catalog.Packs["en-US"].Strings["navigation.home"] == "Updated home", "Replacement import failed.");
        }
        Check(File.ReadAllText(Path.Combine(root, "english.json.bak")).Contains("My home"), "Replacement backup was not retained.");
        Check(Directory.GetFiles(root, "*.tmp").Length == 0, "Import temporary file leaked.");
        snapshot = await store.ReadAsync(CancellationToken.None).ConfigureAwait(false);
        using (var exported = new MemoryStream())
        {
            exported.SetLength(256 * 1024);
            await store.ExportAsync(exported, snapshot.Catalog, "en-US", CancellationToken.None).ConfigureAwait(false);
            Check(exported.Length < 256 * 1024, "Export did not truncate the output.");
            exported.Position = 0; var template = LanguageCatalog.ReadPack(exported);
            baseline.ValidatePack(template); Check(template.Strings.Count == baseline.Fallback.Strings.Count, "Template was incomplete.");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { await store.ReadAsync(cancellation.Token).ConfigureAwait(false); throw new Exception("Canceled read ran."); }
            catch (OperationCanceledException) { }
        }
        File.WriteAllText(Path.Combine(root, "duplicate.json"), Pack("fr-FR", "Duplicate", "navigation.home", "Duplicate"));
        snapshot = await store.ReadAsync(CancellationToken.None).ConfigureAwait(false);
        Check(!snapshot.Catalog.Packs.ContainsKey("fr-FR"), "Duplicate culture was selected arbitrarily.");
        text.ReplaceCatalog(snapshot.Catalog);
        Check(text.Preference == "fr-FR" && text.Languages.Any(choice => choice.Code == "fr-FR"), "Missing selected language ID was lost.");
        File.Delete(Path.Combine(root, "duplicate.json"));
        text.ReplaceCatalog((await store.ReadAsync(CancellationToken.None).ConfigureAwait(false)).Catalog);
        Check(text.Current.Code == "fr-FR" && text["navigation.home"] == "Accueil", "Reinstalled language did not recover.");
        var crowded = Path.Combine(output, "limit-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(crowded);
        for (var index = 0; index < 17; index++) File.WriteAllText(Path.Combine(crowded, index + ".json"), "{}");
        var capped = await new UserLanguageStore(crowded).ReadAsync(CancellationToken.None).ConfigureAwait(false);
        Check(capped.Files.Count == 0 && capped.Issues.Count == 1, "Directory file limit failed.");
        text.SetLanguage("en-US"); for (var index = 0; index < 10000; index++) _ = text["navigation.home"];
        var watch = Stopwatch.StartNew(); var before = GC.GetAllocatedBytesForCurrentThread(); string? last = null;
        for (var index = 0; index < 1000000; index++) last = text["navigation.home"];
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before; watch.Stop();
        Check(allocated == 0 && last == "Updated home", "User language lookup allocated or failed.");
        Console.WriteLine($"User language override/new culture/Chinese aliases, malformed files, format/key/size limits, atomic replacement/backup, export, cancellation and ID recovery passed. 1,000,000 lookups: {watch.Elapsed.TotalMilliseconds:F3} ms, {allocated} bytes.");
    }
    private static MemoryStream Stream(string value) => new(Encoding.UTF8.GetBytes(value));
    private static string Pack(string code, string name, string key, string value)
        => string.Concat("{\"code\":\"", code, "\",\"nativeName\":\"", name, "\",\"strings\":{\"", key, "\":\"", value, "\"}}");
    private static async Task ExpectFailureAsync(Func<Task> action)
    {
        try { await action().ConfigureAwait(false); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid package was accepted.");
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
