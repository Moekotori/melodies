using System.Diagnostics;
using System.Runtime.InteropServices;
using Echo.Application.Scanning;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;
using Microsoft.Win32.SafeHandles;

internal static partial class LargeLibraryFilesProbe
{
    public static async Task RunAsync(string root, CancellationToken cancellation)
    {
        var media = Path.Combine(root, "sparse-metadata"); Directory.CreateDirectory(media);
        const int count = 512;
        const long length = 8L * 1024 * 1024 * 1024;
        for (var i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = Path.Combine(media, $"large-{i:000}.flac");
            AudioFixture.Flac(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            // Never allocate real TBs. Failure to enable Windows sparse mode stops the probe BEFORE SetLength.
            if (OperatingSystem.IsWindows() && !DeviceIoControl(stream.SafeFileHandle, 0x900c4,
                IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new IOException("Unable to enable sparse fixture: " + Marshal.GetLastPInvokeError());
            stream.SetLength(length);
        }
        var files = new LegacyFolderDiscovery().Enumerate(media, true, cancellation);
        long discovered = 0, bytes = 0;
        foreach (var file in files) { discovered++; bytes = checked(bytes + file.SizeBytes); }
        if (discovered != count || bytes != length * count) throw new InvalidOperationException("64-bit discovery failed.");
        var watch = Stopwatch.StartNew();
        await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root, "sparse.sqlite"), cancellation);
        var scan = new ScanCoordinator(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader());
        var first = await scan.ScanAsync(new(media, DetectMissing: true), cancellation: cancellation);
        if (first.Status != ScanStatus.Completed || first.Inserted != count || first.Failed != 0)
            throw new InvalidOperationException("Sparse metadata import failed: " + first.ErrorCode);
        var firstMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        var second = await scan.ScanAsync(new(media, DetectMissing: true), cancellation: cancellation);
        if (second.Unchanged != count || second.Inserted + second.Updated + second.Failed != 0 || second.MarkedMissing != 0)
            throw new InvalidOperationException("Sparse metadata incremental mismatch.");
        var snapshots = new DirectoryDiscovery().Enumerate(media, false, cancellation).Take(128).ToArray();
        var known = await db.FindKnownAsync(snapshots, cancellation);
        if (known.Count != snapshots.Length || known.Values.Any(item => item.SizeBytes != length))
            throw new InvalidOperationException("SQLite truncated a >4 GiB length.");
        Console.WriteLine($"SPARSE_METADATA files={count} logicalBytes={bytes} firstMs={firstMs:F1} incrementalMs={watch.Elapsed.TotalMilliseconds:F1}");
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);

    public static void CueFolder(string root, CancellationToken cancellation)
    {
        var directory = Path.Combine(root, "flat-cue"); Directory.CreateDirectory(directory);
        const int albums = 4100;
        for (var i = 0; i < albums; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var audio = $"disc-{i:0000}.wav";
            AudioFixture.Wave(Path.Combine(directory, audio), "Disc");
            File.WriteAllText(Path.Combine(directory, $"disc-{i:0000}.cue"), $"""
                FILE "{audio}" WAVE
                TRACK 01 AUDIO
                INDEX 01 00:00:00
                TRACK 02 AUDIO
                INDEX 01 00:00:01
                """);
        }
        var priorScratch = Directory.GetFiles(Path.GetTempPath(), "echo-folder-*.sqlite").ToHashSet(StringComparer.Ordinal);
        var watch = Stopwatch.StartNew();
        var count = 0;
        foreach (var file in new LegacyFolderDiscovery().Enumerate(directory, false, cancellation))
        {
            if (file.Issue is not null || file.Source?.Kind != "cue") throw new InvalidOperationException("CUE source leaked: " + file.Issue);
            count++;
        }
        if (count != albums * 2) throw new InvalidOperationException("Large CUE folder lost tracks.");
        if (Directory.GetFiles(Path.GetTempPath(), "echo-folder-*.sqlite").Any(path => !priorScratch.Contains(path)))
            throw new InvalidOperationException("Scratch index leaked after enumeration.");
        // Abandon a spilled enumeration; the iterator's finally must still release its scratch index.
        using (var partial = new LegacyFolderDiscovery().Enumerate(directory, false, cancellation).GetEnumerator())
            for (var i = 0; i < 2100; i++) if (!partial.MoveNext()) throw new InvalidOperationException("Short CUE fixture.");
        if (Directory.GetFiles(Path.GetTempPath(), "echo-folder-*.sqlite").Any(path => !priorScratch.Contains(path)))
            throw new InvalidOperationException("Scratch index leaked on early disposal.");
        Console.WriteLine($"FLAT_CUE albums={albums} tracks={count} elapsedMs={watch.Elapsed.TotalMilliseconds:F1} scratchReleased=true");
    }
}
