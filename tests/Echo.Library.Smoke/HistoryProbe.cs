using System.Diagnostics;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Microsoft.Data.Sqlite;

internal static class HistoryProbe
{
    public static async Task RunAsync(string root, CancellationToken token)
    {
        Directory.CreateDirectory(root); var file = Path.Combine(root, "history-" + Guid.NewGuid().ToString("N") + ".sqlite");
        await VerifyMigrationAsync(root, token);
        var media = Path.Combine(root, "fixture-song.flac"); var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using (var setup = await LibraryDatabase.OpenAsync(file, token))
        {
            var scan = await setup.BeginScanAsync(root, false, token);
            await setup.CommitScanBatchAsync(scan, 1, [new(new(media, LibraryPath.Key(media), 1, 1), ScanEntryKind.Metadata,
                new("夜航 100%", "ECHO", "夜色与回声", "ECHO", 60, "FLAC", 96000, 24))], token);
            await setup.FinishScanAsync(scan, ScanStatus.Completed, 1, false, null, token);
        }
        using (var owner = new FileStream(file + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Pooling = false }.ToString()))
        {
            connection.Open(); connection.CreateFunction<string,string>("echo_fold", x => x.Normalize().ToLowerInvariant());
            using var command = connection.CreateCommand(); command.Parameters.AddWithValue("$now", now);
            command.CommandText = """
                WITH RECURSIVE numbers(n) AS(SELECT 0 UNION ALL SELECT n+1 FROM numbers WHERE n<49999)
                INSERT INTO listening_events(id,track_id,started,updated,seconds,ended,title,artist,album,path,duration,codec,source,history_epoch,counted,sample_rate,bit_depth)
                SELECT printf('seed-%06d',n),t.id,$now-(n/50)*3600,$now-(n/50)*3600+30,
                  CASE WHEN n%5=0 THEN 5 ELSE 30 END,$now-(n/50)*3600+30,
                  t.title,t.artist,t.album,t.path,t.duration,t.codec,'曲目',0,n%5<>0,t.sample_rate,t.bit_depth
                FROM numbers CROSS JOIN tracks t;
                """;
            command.ExecuteNonQuery();
        }
        await using var db = await LibraryDatabase.OpenAsync(file, token);
        var checks = 0; var watch = Stopwatch.StartNew();
        var first = await db.GetHistoryAsync(new(), cancellation: token); var firstMs = watch.Elapsed.TotalMilliseconds;
        Check(first.Total == 50000 && first.Items.Count == 200, "first page");
        var second = await db.GetHistoryAsync(new(), first.Next, cancellation: token);
        Check(!first.Items.Select(x=>x.Id).Intersect(second.Items.Select(x=>x.Id)).Any(), "tied timestamp cursor");
        var back = await db.GetHistoryAsync(new(), second.Previous, cancellation: token);
        Check(first.Items.Select(x=>x.Id).SequenceEqual(back.Items.Select(x=>x.Id)), "backward cursor");
        var search = await db.GetHistoryAsync(new("夜航"), cancellation:token); Check(search.Total == 50000, "short Chinese search");
        var literal = await db.GetHistoryAsync(new("100%"), cancellation:token); Check(literal.Total == 50000, "literal wildcard search");
        var counted = await db.GetHistoryAsync(new(CountedOnly:true), cancellation:token); Check(counted.Total == 40000, "qualified plays");
        var row = first.Items[0].Track; var epoch = await db.GetHistoryEpochAsync(token);
        var entry = new ListeningEntry("zz-live", row.Id, row.Path, now, now, 0, Source:"历史", HistoryEpoch:epoch);
        await db.RecordListeningAsync(entry, token);
        await db.RecordListeningAsync(entry with { PlayedSeconds=30, UpdatedUnix=now+30, EndedUnix=now+30 }, token);
        var done = await db.GetHistoryAsync(new(), cancellation:token);
        Check(done.Items[0] is { Counted:true, EndedUnix:not null, Track.SampleRate:96000, Source:"历史" }, "saved completion and quality");
        var frozen = await db.GetHistoryAsync(new(), first.Next, cancellation:token); Check(frozen.Items.All(x=>x.Id!=entry.Id), "snapshot insert isolation");
        var pending = entry with { Id="zz-pending" }; await db.RecordListeningAsync(pending,token);
        var mark = await db.BeginScanAsync(root,false,token);
        await db.CommitScanBatchAsync(mark,1,[new(new(media,LibraryPath.Key(media),1,2),ScanEntryKind.Metadata,
            new("Renamed","ECHO","Renamed album","ECHO",60,"MP3",44100,0))],token);
        await db.FinishScanAsync(mark,ScanStatus.Completed,1,false,null,token);
        var oldSearch = await db.GetHistoryAsync(new("夜航"),cancellation:token); Check(oldSearch.Total==50002 && oldSearch.Items[0].Track.Codec=="FLAC", "immutable metadata snapshots");
        watch.Restart(); var review = await db.GetHistoryReviewAsync(new(),token); var reviewMs=watch.Elapsed.TotalMilliseconds;
        Check(review.Totals.Plays==50002 && review.Totals.Counted==40001 && review.Quality.Count==1 && review.Days.Count<=366, "review totals and bounds");
        var bounded = await db.GetHistoryReviewAsync(new(FromUnix:now-86400,ToUnix:now+86400),token);
        Check(bounded.Previous is not null, "elapsed period comparison");
        using(var cancelled=new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await db.GetHistoryAsync(new(),cancellation:cancelled.Token); throw new Exception("cancel ignored"); }
            catch(OperationCanceledException) { checks++; }
        }
        var absent=await db.BeginScanAsync(root,true,token); await db.FinishScanAsync(absent,ScanStatus.Completed,0,true,null,token);
        var invalid=await db.GetHistoryAsync(new(),cancellation:token); Check(invalid.Items.All(x=>!x.Available),"invalid snapshots retained");
        Check(await db.PruneInvalidHistoryAsync(token)==50001,"prune ended invalid records, keep pending");
        Check(await db.ClearHistoryAsync(token)==1,"clear remaining pending");
        await db.RecordListeningAsync(pending with { PlayedSeconds=45,EndedUnix=now+45,UpdatedUnix=now+45 },token);
        Check((await db.GetHistoryAsync(new(),cancellation:token)).Total==0,"epoch rejects late active write");
        try { await db.GetHistoryAsync(new(),first.Next,cancellation:token); throw new Exception("stale cursor accepted"); }
        catch(LibraryChangedException) { checks++; }
        var report=$"Checks: {checks}\nFixture: 50000 generated events / one track, isolated SQLite\nFirst page: {firstMs:F2} ms (1 sample)\nReview: {reviewMs:F2} ms (1 sample)\n";
        await File.WriteAllTextAsync(Path.Combine(root,"results.txt"),report,token); Console.WriteLine(report);
        void Check(bool ok,string label) { if(!ok) throw new InvalidOperationException(label); checks++; }
    }
    private static async Task VerifyMigrationAsync(string root, CancellationToken token)
    {
        var path = Path.Combine(root,"upgrade-"+Guid.NewGuid().ToString("N")+".sqlite");
        var media = Path.Combine(root,"upgrade.flac");
        await using(var seed=await LibraryDatabase.OpenAsync(path,token))
        {
            var job=await seed.BeginScanAsync(root,false,token);
            await seed.CommitScanBatchAsync(job,1,[new(new(media,LibraryPath.Key(media),1,1),ScanEntryKind.Metadata,new("旧记录","ECHO","Album","ECHO",40,"FLAC",48000,24))],token);
            await seed.FinishScanAsync(job,ScanStatus.Completed,1,false,null,token);
            var track=(await seed.GetTracksAsync(new(),token)).Items[0];
            await seed.RecordListeningAsync(new("legacy",track.Id,media,100,125,25),token);
        }
        using(var owner=new FileStream(path+".writer.lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
        using(var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=path,Pooling=false}.ToString()))
        {
            connection.Open(); using var command=connection.CreateCommand();
            command.CommandText="SELECT name FROM sqlite_master WHERE type='trigger' AND (sql LIKE '%listening_events%' OR sql LIKE '%history_state%')";
            var names=new List<string>(); using(var reader=command.ExecuteReader()) while(reader.Read()) names.Add(reader.GetString(0));
            foreach(var name in names) { command.CommandText="DROP TRIGGER \""+name.Replace("\"","\"\"")+"\""; command.ExecuteNonQuery(); }
            command.CommandText="DROP INDEX history_page; DROP INDEX history_counted_page; DROP TABLE history_search; DROP TABLE history_state;"; command.ExecuteNonQuery();
            foreach(var column in new[]{"sample_rate","bit_depth","ended","title","artist","album","path","duration","codec","source","history_epoch","counted"})
            { command.CommandText="ALTER TABLE listening_events DROP COLUMN "+column; command.ExecuteNonQuery(); }
            command.CommandText="PRAGMA user_version=13"; command.ExecuteNonQuery();
        }
        await using var upgraded=await LibraryDatabase.OpenAsync(path,token);
        var page=await upgraded.GetHistoryAsync(new(),cancellation:token);
        if(page.Items.Count!=1 || page.Items[0] is not {Id:"legacy",Counted:true,EndedUnix:125,Track.Title:"旧记录",Track.SampleRate:48000})
            throw new InvalidOperationException("v13 history migration lost identity, completion or metadata");
        if(!Directory.EnumerateFiles(root,Path.GetFileName(path)+".before-v14-*.sqlite").Any()) throw new InvalidOperationException("Migration backup missing");
        Console.WriteLine("v13→v16 history migration, snapshot backfill, counted threshold and backup passed.");
    }
}
