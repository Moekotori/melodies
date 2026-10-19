using System.Diagnostics;
using System.Text;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;
using Microsoft.Data.Sqlite;
using static WaveFixtures;

internal static class WaveCompatibilityProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        var timer = Stopwatch.StartNew(); var checks = 0; var sequence = 0;
        void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
        TrackMetadata Read(string path) => WaveMetadataReader.Read(AudioFixture.Snapshot(path), token);
        string Fixture(string container = "RIFF", params Chunk[] chunks)
        { var path = Path.Combine(root, (++sequence) + ".wav"); Write(path, container, chunks); return path; }
        void Reject(string path, string label)
        {
            try { Read(path); throw new Exception("Accepted invalid " + label); }
            catch (MetadataReadException) { checks++; }
            catch (EndOfStreamException) { checks++; }
        }
        foreach (var (bits, channels, encoding, valid) in new[]
        { (8, 1, 1, 0), (16, 2, 1, 0), (24, 2, 1, 0), (32, 2, 1, 0), (64, 2, 1, 0),
            (32, 2, 3, 0), (64, 2, 3, 0), (24, 6, 1, 20), (32, 8, 1, 24), (32, 6, 3, 32) })
        {
            var bytes = new byte[480 * channels * bits / 8];
            var data = Read(Fixture("RIFF", new("fmt ", Format(bits, channels, encoding, valid)), new("data", bytes)));
            Check(data.BitDepth == (valid > 0 ? valid : bits) && data.SampleRate == 48000 &&
                data.DurationSeconds == 0.01 && data.Codec == (encoding == 3 ? "PCM_FLOAT" : "PCM"), "PCM/float/extensible properties");
        }
        Chunk[] basic = [new("fmt ", Format()), new("data", new byte[1920])];
        foreach (var container in new[] { "RIFF", "RF64", "BW64" })
        {
            var data = Read(Fixture(container, basic[0], new("JUNK", [1, 2, 3], container != "RIFF"),
                new("JUNK", [4, 5, 6, 7], container != "RIFF"), basic[1] with { Large = container != "RIFF" },
                Info(("TITL", Text("Late title")), ("IRPD", Text("Album")), ("GNRE", Text("Jazz")),
                    ("YEAR", Text("1999-10-01")), ("IPRT", Text("3/12")))));
            Check(data.Title == "Late title" && data.Album == "Album" && data.Genre == "Jazz" && data.Year == 1999 && data.TrackNumber == 3,
                container + " late INFO/aliases/repeated ds64 entries/odd padding");
        }
        var canonical = Read(Fixture("RIFF", [..basic, Info(("INAM", Text("Canonical")), ("TITL", Text("Alias")))]));
        Check(canonical.Title == "Canonical", "canonical INFO precedence");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var (encoding, value, bom) in new[]
        { (Encoding.Unicode, "UTF16 title", false), (Encoding.BigEndianUnicode, "UTF16 title", true),
            (Encoding.Unicode, "中文 标题", true), (new UTF8Encoding(true), "Кириллица 日本語", true),
            (Encoding.GetEncoding(936), "中文标题", false), (Encoding.GetEncoding(932), "テスト音楽", false) })
        {
            var text = encoding.GetBytes(value + "\0"); if (bom) text = [..encoding.GetPreamble(), ..text];
            Check(Read(Fixture("RIFF", [..basic, Info(("INAM", text))])).Title == value, "INFO text " + encoding.WebName);
        }
        foreach (var page in new[] { 1252, 936 })
        {
            var value = page == 1252 ? "Café déjà vu" : "简体中文";
            var cset = new byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(cset, (ushort)page);
            Check(Read(Fixture("RIFF", [..basic, Info(("INAM", Encoding.GetEncoding(page).GetBytes(value + "\0"))), new("CSET", cset)])).Title == value,
                "late CSET " + page);
        }
        var broadcast = Read(Fixture("BW64", [..basic, Bext()]));
        Check(broadcast is { Title: "Broadcast title", Artist: "Studio", Year: 2003 }, "bext fallback");
        var overrideTags = Read(Fixture("RIFF", [..basic, Info(("INAM", Text("INFO")), ("IART", Text("Artist")), ("ICRD", Text("2024"))), Bext()]));
        Check(overrideTags is { Title: "INFO", Artist: "Artist", Year: 2024 }, "INFO overrides bext");
        var trailing = Fixture("RIFF", basic); using (var stream = new FileStream(trailing, FileMode.Append)) stream.Write("trailing outside RIFF"u8);
        Check(Read(trailing).DurationSeconds == 0.01, "respect RIFF boundary");
        var sparse = Path.Combine(root, "large.wav"); const long large = 4L * 1024 * 1024 * 1024 + 1920;
        Sparse(sparse, large); var sparseData = Read(sparse);
        Check(sparseData.Title == "After 4 GiB" && sparseData.DurationSeconds == (double)large / 192000, "64-bit data seek and late tags");
        foreach (var sync in new uint[] { 0x7ffe8001, 0xfe7f0180, 0x1fffe800, 0xff1f00e8, 0x72f81f4e, 0xf8724e1f })
        {
            var bytes = new byte[64]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(2), sync);
            Check(WavePayloadProbe.IsEncoded(bytes), "DTS/SPDIF sync " + sync.ToString("X"));
        }
        Check(!WavePayloadProbe.IsEncoded(new byte[64]), "plain PCM sniff");

        var alignment = Format(); alignment[12] = 2;
        Reject(Fixture("RIFF", new("fmt ", alignment), basic[1]), "alignment");
        var ext = Format(32, 2, 1, 24); ext[16] = 0;
        Reject(Fixture("RIFF", new("fmt ", ext), basic[1]), "extensible cbSize");
        ext = Format(32, 2, 1, 24); ext[18] = 33;
        Reject(Fixture("RIFF", new("fmt ", ext), basic[1]), "extensible valid bits");
        Reject(Fixture("RIFF", basic[0], new("data", [0])), "partial PCM frame");
        Reject(Fixture("RIFF", new Chunk("fmt ", [1, 2])), "short fmt");
        var truncated = Fixture("RIFF", basic); Patch(truncated, 40, 100000);
        Reject(truncated, "data beyond RIFF");
        var noDs64 = Fixture("RF64", basic); Patch(noDs64, 12, 0);
        Reject(noDs64, "missing first ds64");
        var badDs64 = Fixture("BW64", basic); Patch(badDs64, 20, uint.MaxValue);
        Reject(badDs64, "ds64 beyond file");
        var missingSize = Fixture("RF64", basic[0], basic[1], new("JUNK", [1, 2], true)); Patch(missingSize, 48, 0);
        Reject(missingSize, "missing ds64 table entry");
        var malformedTag = Info(("INAM", Text("Title"))); malformedTag.Bytes[8] = 255;
        Reject(Fixture("RIFF", [..basic, malformedTag]), "tag boundary");
        Reject(Fixture("RIFF", [..basic, Info(("INAM", new byte[16385]))]), "tag budget");
        await ImportAsync(Path.Combine(root, "import"), token, Check);
        Console.WriteLine($"WAVE_COMPATIBILITY_PASSED checks={checks} sparseBytes={large} elapsedMs={timer.ElapsedMilliseconds}");
    }

    private static async Task ImportAsync(string root, CancellationToken token, Action<bool, string> check)
    {
        var media = Path.Combine(root, "media"); Directory.CreateDirectory(media);
        AudioFixture.Wave(Path.Combine(media, "old.wav"), "WAV"); AudioFixture.Flac(Path.Combine(media, "unchanged.flac"));
        var dbPath = Path.Combine(root, "library.sqlite");
        await using (var db = await LibraryDatabase.OpenAsync(dbPath, token))
        {
            var scan = new ScanCoordinator(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader());
            var first = await scan.ScanAsync(new(media), cancellation: token);
            check(first is { Status: ScanStatus.Completed, Inserted: 2, Failed: 0 }, "WAV/FLAC import");
        }
        using (var connection = new SqliteConnection("Data Source=" + dbPath))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE tracks SET parser_version=3"; command.ExecuteNonQuery();
        }
        Write(Path.Combine(media, "broken.wav"), "RIFF", new Chunk("fmt ", [1]));
        await using (var db = await LibraryDatabase.OpenAsync(dbPath, token))
        {
            var scan = new ScanCoordinator(db, new LegacyFolderDiscovery(db), () => new LocalMetadataReader());
            var next = await scan.ScanAsync(new(media, DetectMissing: true), cancellation: token);
            check(next is { Status: ScanStatus.Completed, Updated: 1, Unchanged: 1, Failed: 1, MarkedMissing: 0 }, "selective WAV upgrade and corrupt file isolation");
            var repeat = await scan.ScanAsync(new(media), cancellation: token);
            check(repeat is { Status: ScanStatus.Completed, Updated: 0, Unchanged: 2, Failed: 1 }, "WAV incremental reuse");
        }
        var file = AudioFixture.Snapshot(Path.Combine(media, "old.wav"));
        check(TrackMetadata.VersionFor(file with { Path = "disc.cue#1", Source = new("cue", file.Path, "disc.cue", "v5", file.SizeBytes, file.ModifiedTicks, 0, 0) }) == 4,
            "WAV-backed CUE selective upgrade");
    }
}
