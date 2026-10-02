using System.Text;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Core.Media;
using Echo.Core.Protocol;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class LegacyMigrationProbe
{
    public static async Task<int> RunAsync(string root, CancellationToken cancellation)
    {
        Directory.CreateDirectory(root);
        var checks = 0;
        void Check(bool value, string label)
        { if (!value) throw new InvalidOperationException(label); checks++; }
        var cueDir = Path.Combine(root,"cue"); Directory.CreateDirectory(cueDir);
        var audio = Path.Combine(cueDir,"disc.wav"); Wave(audio,3);
        var cue = Path.Combine(cueDir,"album.cue");
        await File.WriteAllTextAsync(cue, """
            TITLE "旧版专辑"
            PERFORMER "旧版艺术家"
            FILE "disc.wav" WAVE
            TRACK 01 AUDIO
            TITLE "第一首"
            INDEX 01 00:00:00
            TRACK 02 AUDIO
            TITLE "第二首"
            INDEX 00 00:00:70
            INDEX 01 00:01:00
            TRACK 03 AUDIO
            TITLE "第三首"
            INDEX 01 00:02:00
            """, new UTF8Encoding(true), cancellation);
        var legacy = ScanCueReader.Read(new ScanCueRequest { FilePath=cue,RootPath=root,IsolateFileErrors=true },cancellation);
        Check(legacy.Tracks.Count==3 && legacy.Tracks[0].CueTrack!.EndSeconds==1,"copied CUE facts");
        await using var db = await LibraryDatabase.OpenAsync(Path.Combine(root,"library.sqlite"),cancellation);
        ScanCoordinator Scanner() => new(db,new LegacyFolderDiscovery(db),()=>new LocalMetadataReader());
        var first = await Scanner().ScanAsync(new(cueDir),cancellation:cancellation);
        Check(first.Failed==0 && first.Inserted==3,"CUE import");
        var page = await db.GetTracksAsync(new(20),cancellation);
        Check(page.Items.Count==3 && page.Items.All(item=>item.Source?.Kind=="cue"),"no whole-album duplicate");
        Check(page.Items.All(item=>Math.Abs(item.DurationSeconds-1)<.001),"segment durations");
        var source = TrackPlaybackSource.Audio(page.Items.Single(item=>item.Title=="第二首"));
        Check(source.Uri==audio && source.Range?.StartSeconds==1 && source.Range.EndSeconds==2,"playback range");
        var again = await Scanner().ScanAsync(new(cueDir),cancellation:cancellation);
        Check(again.Unchanged==3 && again.Updated==0,"CUE incremental");
        File.Delete(cue);
        var restored = await Scanner().ScanAsync(new(cueDir),cancellation:cancellation);
        Check(restored.Inserted==1 && (await db.GetTracksAsync(new(20),cancellation)).Items.Count==1,"CUE removal restores physical track");

        var osuDir = Path.Combine(root,"osu"); Directory.CreateDirectory(osuDir);
        Wave(Path.Combine(osuDir,"audio.wav"),3);
        for(var i=0;i<12;i++) await File.WriteAllTextAsync(Path.Combine(osuDir,$"map{i:00}.osu"),$"""
            osu file format v14
            [General]
            AudioFilename: audio.wav
            [Metadata]
            Title: ASCII title
            TitleUnicode: 谱面标题
            Artist: ASCII artist
            ArtistUnicode: 谱面艺术家
            Version: Hard
            BeatmapID: {i+1}
            BeatmapSetID: 42
            [TimingPoints]
            0,500,4,1,0,100,1,0
            [HitObjects]
            1,2,3,4,5
            """, cancellation);
        var osu = await Scanner().ScanAsync(new(osuDir),cancellation:cancellation);
        Check(osu.Inserted==1 && osu.Failed==0,"shared osu audio deduplicated");
        page = await db.GetTracksAsync(new(20,"谱面标题"),cancellation);
        Check(page.Items.Count==1 && page.Items[0].Artist=="谱面艺术家" && page.Items[0].Source?.Bpm==120,"osu overlay");
        var cached = await Scanner().ScanAsync(new(osuDir),cancellation:cancellation);
        Check(cached.Unchanged==1 && cached.Failed==0,"cached osu incremental");
        var map = Path.Combine(osuDir,"map00.osu");
        var text = await File.ReadAllTextAsync(map,cancellation);
        await File.WriteAllTextAsync(map,text.Replace("谱面标题","更新的标题"),cancellation);
        File.SetLastWriteTimeUtc(map,DateTime.UtcNow.AddSeconds(3));
        var updated = await Scanner().ScanAsync(new(osuDir),cancellation:cancellation);
        Check(updated.Updated==1 && (await db.GetTracksAsync(new(10,"更新的标题"),cancellation)).Items.Count==1,"beatmap dependency invalidation");

        var brokenDir = Path.Combine(root,"broken"); Directory.CreateDirectory(brokenDir);
        Wave(Path.Combine(brokenDir,"other.wav"),3);
        await File.WriteAllTextAsync(Path.Combine(brokenDir,"bad.cue"),"FILE \"gone.wav\" WAVE\nTRACK 01 AUDIO\nINDEX 01 00:00:00",cancellation);
        var broken = await Scanner().ScanAsync(new(brokenDir,DetectMissing:true),cancellation:cancellation);
        Check(broken.Failed>0 && broken.MarkedMissing==0 && broken.Inserted==0,"broken CUE directory deferral");

        var rates = Path.Combine(root,"rates"); Directory.CreateDirectory(rates);
        Wave(Path.Combine(rates,"audio.wav"),3); Wave(Path.Combine(rates,"audio 2x.wav"),3);
        foreach (var item in new[] { ("a.osu","audio.wav","Hard",500), ("b.osu","audio 2x.wav","Hard 2x",250) })
            await File.WriteAllTextAsync(Path.Combine(rates,item.Item1),$"""
                [General]
                AudioFilename: {item.Item2}
                [Metadata]
                Title: Same song
                Artist: Same artist
                Version: {item.Item3}
                [TimingPoints]
                0,{item.Item4},4,1,0,100,1,0
                """,cancellation);
        var rateReport = await Scanner().ScanAsync(new(rates),cancellation:cancellation);
        Check(rateReport.Inserted==1 && rateReport.Failed==0,"legacy original-rate preference");

        var embeddedDir = Path.Combine(root,"embedded"); Directory.CreateDirectory(embeddedDir);
        var embeddedPath = Path.Combine(embeddedDir,"embedded.flac");
        var cueText = "TITLE \"内嵌专辑\"\nTRACK 01 AUDIO\nTITLE \"片段一\"\nINDEX 01 00:00:00\nTRACK 02 AUDIO\nTITLE \"片段二\"\nINDEX 01 00:01:00";
        using (var stream=File.Create(embeddedPath))
        using (var writer=new BinaryWriter(stream,Encoding.UTF8))
        {
            writer.Write("fLaC"u8);writer.Write(new byte[] {0,0,0,34});
            var info=new byte[34];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(info.AsSpan(10),((ulong)48000<<44)|((ulong)1<<41)|((ulong)15<<36)|144000UL);
            writer.Write(info);
            using var comments=new MemoryStream();
            using(var tags=new BinaryWriter(comments,Encoding.UTF8,true))
            {
                tags.Write(4);tags.Write("ECHO"u8);tags.Write(1);
                var bytes=Encoding.UTF8.GetBytes("CUESHEET="+cueText);tags.Write(bytes.Length);tags.Write(bytes);
            }
            writer.Write((byte)0x84);writer.Write(new byte[]{(byte)(comments.Length>>16),(byte)(comments.Length>>8),(byte)comments.Length});
            writer.Write(comments.ToArray());
        }
        var embedded = await Scanner().ScanAsync(new(embeddedDir),cancellation:cancellation);
        Check(embedded.Inserted==2 && embedded.Failed==0,"copied embedded FLAC CUE");
        var segments=await db.GetTracksAsync(new(10,"片段"),cancellation);
        Check(segments.Items.Count==2 && segments.Items.All(row=>row.Source?.AudioPath==embeddedPath),"embedded playback origins");

        var native = Path.Combine(AppContext.BaseDirectory,"runtimes","win-x64","native","echo-native-scanner.exe");
        if (OperatingSystem.IsWindows())
        {
            Check(File.Exists(native),"bundled native scanner");
            await using var cpp = new NativeMetadataReader(native);
            var metadata = await cpp.ReadAsync(AudioFixture.Snapshot(audio),cancellation);
            Check(metadata.SampleRate==48000 && Math.Abs(metadata.DurationSeconds-3)<.001,"copied C++ scanner executes");
        }
        return checks;
    }
    private static void Wave(string path,int seconds)
    {
        using var stream=File.Create(path); using var writer=new BinaryWriter(stream,Encoding.UTF8);
        writer.Write("RIFF"u8);writer.Write(36+seconds*192000);writer.Write("WAVE"u8);
        writer.Write("fmt "u8);writer.Write(16);writer.Write((ushort)1);writer.Write((ushort)2);
        writer.Write(48000);writer.Write(192000);writer.Write((ushort)4);writer.Write((ushort)16);
        writer.Write("data"u8);writer.Write(seconds*192000);writer.Write(new byte[seconds*192000]);
    }
}
