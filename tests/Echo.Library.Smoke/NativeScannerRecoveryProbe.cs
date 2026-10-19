using System.Runtime.InteropServices;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class NativeScannerRecoveryProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var scanner = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "Echo.Library.Smoke.exe" : "Echo.Library.Smoke");
        var state = Path.Combine(root, "attempts.txt");
        var priorState = Environment.GetEnvironmentVariable(ScannerFaultHost.StateVariable);
        var priorMode = Environment.GetEnvironmentVariable(ScannerFaultHost.ModeVariable);
        var priorRuntime = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        var priorRuntimeX64 = Environment.GetEnvironmentVariable("DOTNET_ROOT_X64");
        try
        {
            var runtimeRoot = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            Environment.SetEnvironmentVariable("DOTNET_ROOT", runtimeRoot);
            if (RuntimeInformation.ProcessArchitecture == Architecture.X64) Environment.SetEnvironmentVariable("DOTNET_ROOT_X64", runtimeRoot);
            Environment.SetEnvironmentVariable(ScannerFaultHost.StateVariable, state);
            Environment.SetEnvironmentVariable(ScannerFaultHost.ModeVariable, "recover");
            var audio = Path.Combine(root, "one.wav"); CueFixtures.Wave(audio);
            var file = AudioFixture.Snapshot(audio);
            await using (var reader = new NativeMetadataReader(scanner))
            {
                for (var i = 0; i < 3; i++)
                {
                    try { await reader.ReadAsync(file, token); throw new Exception("Crash fixture did not crash"); }
                    catch (MetadataReadException error) when (error.Code == "native_scanner_exited") { }
                }
                Check((await reader.ReadAsync(file, token)).Title == "Healthy", "three crashed files poisoned the next healthy source");
            }
            Check(File.ReadAllText(state) == "4", "recovery replayed a failed request");
            Environment.SetEnvironmentVariable(ScannerFaultHost.ModeVariable, "crash");
            File.Delete(state);
            for (var i = 0; i < 11; i++) await File.WriteAllTextAsync(Path.Combine(root, i + ".mp3"), "fixture", token);
            await using (var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "recovery-limit.sqlite"), token))
            {
                var report = await new ScanCoordinator(db, new DirectoryDiscovery(), () => new NativeMetadataReader(scanner))
                    .ScanAsync(new(root, Workers: 1, BatchSize: 16, DetectMissing: true), cancellation: token);
                Check(report.Status == ScanStatus.Failed && report.ErrorCode == "native_recovery_limit" && report.MarkedMissing == 0
                    && File.ReadAllText(state) == "8", "restart budget must stop the task instead of silently rejecting all remaining files");
            }
            Environment.SetEnvironmentVariable(ScannerFaultHost.ModeVariable, "protocol");
            File.Delete(state);
            await using (var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "protocol.sqlite"), token))
            {
                var report = await new ScanCoordinator(db, new DirectoryDiscovery(), () => new NativeMetadataReader(scanner))
                    .ScanAsync(new(root, Workers: 1, DetectMissing: true), cancellation: token);
                Check(report.Status == ScanStatus.Failed && report.ErrorCode == "native_protocol_unsupported" && !File.Exists(state),
                    "incompatible scanner was treated as a bad media file");
            }
            await using (var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "cleanup.sqlite"), token))
            {
                var report = await new ScanCoordinator(db, new DirectoryDiscovery(), () => new CleanupFailure())
                    .ScanAsync(new(root, Workers: 1, DetectMissing: true), cancellation: token);
                Check(report.Status == ScanStatus.Failed && report.ErrorCode == "native_cleanup_unconfirmed" && report.MarkedMissing == 0,
                    "unconfirmed native cleanup must stop the pipeline before replacement work");
            }
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(token);
            await using (var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "cancel-cleanup.sqlite"), token))
            {
                var report = await new ScanCoordinator(db, new DirectoryDiscovery(), () => new CleanupFailure(stopped.Cancel))
                    .ScanAsync(new(root, Workers: 1, DetectMissing: true), cancellation: stopped.Token);
                Check(report.Status == ScanStatus.Failed && report.ErrorCode == "native_cleanup_unconfirmed" && report.MarkedMissing == 0,
                    "user cancellation hid unconfirmed process cleanup");
            }
            Console.WriteLine("PASS real scanner subprocess recovery: three crashes then healthy, eight-attempt limit, incompatible protocol, and fatal cleanup policy");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScannerFaultHost.StateVariable, priorState);
            Environment.SetEnvironmentVariable(ScannerFaultHost.ModeVariable, priorMode);
            Environment.SetEnvironmentVariable("DOTNET_ROOT", priorRuntime);
            Environment.SetEnvironmentVariable("DOTNET_ROOT_X64", priorRuntimeX64);
        }
    }

    private sealed class CleanupFailure(Action? before = null) : IMetadataReader
    {
        public ValueTask<TrackMetadata> ReadAsync(FileSnapshot file, CancellationToken token)
        { before?.Invoke(); throw new MetadataReadException("native_cleanup_unconfirmed"); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
