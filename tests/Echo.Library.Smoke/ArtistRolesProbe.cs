using System.Diagnostics;
using Echo.Application.Scanning;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;
using Echo.Infrastructure.Metadata;

internal static class ArtistRolesProbe
{
    public static async Task RunAsync(string root)
    {
        Directory.CreateDirectory(root);
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var token = budget.Token;
        var file = Path.Combine(root,"credits.flac");
        AudioFixture.Flac(file, ["TITLE=Role sample", "ALBUM=Shared", "ARTIST=Alpha::Beta", "ARTIST=Gamma\0Delta",
            "ALBUMARTIST=Alpha::Beta", "COMPOSER=Writer", "CONDUCTOR=Leader", "ENSEMBLE=Orchestra",
            "PERSONNEL=Player - Flute", "PERSONNEL=Player - Producer", "PERSONNEL=Hyphen - Name - Guitar", "PERSONNEL=Other - Custom Craft"]);
        var original = File.ReadAllBytes(file);
        var metadata = FlacMetadataReader.Read(AudioFixture.Snapshot(file),token);
        Check(metadata.Credits?.Count == 10,"Repeated artist and role tags retained from FLAC.");
        var source=new TrackSource("cue",file,Path.Combine(root,"album.cue"),"stamp",100,100,100,100,Artist:"CUE Artist",AlbumArtist:"CUE Album",EndSeconds:1);
        var virtualRead=await new SourceMetadataReader().ReadAsync(new(source.DefinitionPath+"#cueTrack=1","virtual",100,100),source,
            (_,_,_)=>ValueTask.FromResult(metadata),token);
        Check(virtualRead.Artist=="CUE Artist" && virtualRead.Credits!.All(tag=>!tag.Tag.Equals("ARTIST",StringComparison.OrdinalIgnoreCase)
            && !tag.Tag.Equals("ALBUMARTIST",StringComparison.OrdinalIgnoreCase)) && virtualRead.Credits!.Any(tag=>tag.Tag=="COMPOSER"),"Virtual overrides replace inherited main credits while retaining roles.");
        await using (var native = new NativeMetadataReader(Path.GetFullPath("artifacts/native/scanner/win-x64/echo-native-scanner.exe")))
        {
            var read = await native.ReadAsync(AudioFixture.Snapshot(file),token);
            Check(read.Credits?.Count == 10 && read.Credits.Any(tag => tag.Tag.Equals("personnel",StringComparison.OrdinalIgnoreCase) && tag.Value == "Player - Flute"),"Native FLAC retains raw personnel values.");
        }
        var dbPath = Path.Combine(root,"roles.sqlite");
        await using (var db = await LibraryDatabase.OpenAsync(dbPath,token))
        {
            var job = await db.BeginScanAsync(root,false,token);
            var entries = Enumerable.Range(0,250).Select(index => new ScanEntry(
                new(Path.Combine(root,index+".flac"),LibraryPath.Key(Path.Combine(root,index+".flac")),100,100),ScanEntryKind.Metadata,
                metadata with { Title = "Track " + index })).ToArray();
            await db.CommitScanBatchAsync(job,1,entries,token);
            await db.FinishScanAsync(job,ScanStatus.Completed,250,false,null,token);
            var track = (await db.GetTracksAsync(new(),token)).Items[0];
            await db.SetTrackFavoriteAsync(track.Id,true,token);
            Check((await db.GetArtistsAsync(new(),token)).Items.Select(item => item.Name).Order().SequenceEqual(new[]{"Alpha::Beta","Gamma","Delta"}.Order()),"Default does not guess colon separators; NUL and independent tags work.");
            var credits = await db.GetTrackCreditsAsync(track.Id,cancellation:token);
            Check(credits.Items.Count(item => item.Name == "Player") == 2,"One person can hold multiple roles.");
            Check(credits.Items.Single(item => item.Name == "Hyphen - Name").Role == "Guitar","Personnel uses the final name-role boundary.");
            Check(credits.Items.Single(item => item.Name == "Other").Category == CreditCategory.Unknown,"Unknown roles retain their text and category.");
            Check((await db.GetPeopleAsync(new(Category:CreditCategory.Composer),token)).Items.Single().Name == "Writer","Composer role filter.");
            var player = (await db.GetPeopleAsync(new(Search:"Player"),token)).Items.Single();
            var appearances = await db.GetPersonCreditsAsync(new(player.Id,Limit:1,Category:CreditCategory.Production),token);
            Check(appearances.Total == 250 && appearances.Items.Single().Role == "Producer","Reverse role query is paged and scoped.");
            var oldRevision = credits.Revision;
            using (var cancel = new CancellationTokenSource())
            {
                await ExpectAsync<OperationCanceledException>(() => db.RebuildArtistCreditsAsync(new(0,["::",":"]),
                    new DirectProgress(value => { if(value.Processed>=200) cancel.Cancel(); }),cancel.Token));
            }
            Check((await db.GetArtistImportSettingsAsync(token)).Revision == 0 && (await db.GetTrackCreditsAsync(track.Id,cancellation:token)).Revision == oldRevision,"Canceled rebuild rolls back configuration and both derived indexes.");
            var watch = Stopwatch.StartNew(); var allocation = GC.GetTotalAllocatedBytes();
            await db.RebuildArtistCreditsAsync(new(0,["::",":"]),new DirectProgress(_=>{}),token);
            Console.WriteLine($"CREDIT_REBUILD rows=250 elapsedMs={watch.Elapsed.TotalMilliseconds:F1} allocatedBytes={GC.GetTotalAllocatedBytes()-allocation}");
            var songs = await db.GetArtistsAsync(new(),token);
            Check(songs.Items.Count == 4 && songs.Items.All(item => item.Name != "Writer"),"Custom longest-first separators rebuild song artists without adding role-only people.");
            Check((await db.GetArtistsAsync(new(Grouping:ArtistGrouping.AlbumArtist),token)).Items.Single().Name == "Alpha::Beta","Album artist display stays literal.");
            Check((await db.GetTrackCreditsAsync(track.Id,cancellation:token)).Items.Count(item=>item.Scope==CreditScope.Album)==2,"Album main performers retain album scope.");
            await ExpectAsync<ArtistImportChangedException>(()=>db.RebuildArtistCreditsAsync(new(0,["/"]),new DirectProgress(_=>{}),token));
            await ExpectAsync<LibraryChangedException>(()=>db.GetPersonCreditsAsync(new(player.Id,Revision:oldRevision),token));
            await ExpectAsync<ArgumentException>(()=>db.RebuildArtistCreditsAsync(new(1,["\0"]),new DirectProgress(_=>{}),token));
            await db.RebuildArtistCreditsAsync(new(1,[]),new DirectProgress(_=>{}),token);
            Check((await db.GetArtistsAsync(new(),token)).Items.Count==3,"Removing delimiters preserves repeated tags and fixed NUL boundaries.");
            Check((await db.GetTracksAsync(new(Limit:100),token)).Items.Single(item=>item.Favorite).Id==track.Id,"Favorites and track identity survive rebuilds.");
            var updated = entries[0] with { ExistingId=(await db.GetTracksAsync(new(Search:"Track 0"),token)).Items.Single(item=>item.Title=="Track 0").Id,
                File=entries[0].File with { ModifiedTicks=101 },Metadata=metadata with { Credits=[new("ARTIST","Replacement")],Artist="Replacement" } };
            var rescan=await db.BeginScanAsync(root,false,token);
            await db.CommitScanBatchAsync(rescan,1,[updated],token);
            await db.FinishScanAsync(rescan,ScanStatus.Completed,1,false,null,token);
            Check((await db.GetTrackCreditsAsync(updated.ExistingId!,cancellation:token)).Items.All(item=>item.Name!="Player"),"Rescan replaces stale raw tags and relationships.");
        }
        await using(var reopened=await LibraryDatabase.OpenAsync(dbPath,token))
            Check((await reopened.GetArtistImportSettingsAsync(token)) is { Revision:2,Delimiters.Count:0 },"Settings persist across reopening.");
        Check(File.ReadAllBytes(file).AsSpan().SequenceEqual(original),"No source tag writes.");
        var failures=Path.Combine(root,"failure-isolation"); Directory.CreateDirectory(failures);
        AudioFixture.Flac(Path.Combine(failures,"bad.flac"),["TITLE=Bad",..Enumerable.Range(0,65).Select(i=>"PERSONNEL=Person - Role"+i)]);
        AudioFixture.Flac(Path.Combine(failures,"good.flac"),["TITLE=Good","ARTIST=Good Artist","COMPOSER=Good Composer"]);
        await using(var isolated=await LibraryDatabase.OpenAsync(Path.Combine(root,"failures.sqlite"),token))
        {
            var scanner=new ScanCoordinator(isolated,new LegacyFolderDiscovery(isolated),()=>new LocalMetadataReader());
            var report=await scanner.ScanAsync(new(failures,Workers:1),cancellation:token);
            Check(report.Status==ScanStatus.Completed && report.Failed==1 && report.Inserted==1,"Bad credit budget is a file-local failure; subsequent media still imports.");
        }
        Console.WriteLine("PASS credit roles: native/managed tags, multiple roles, six-category filtering, reverse paging, custom delimiters, cancellation rollback, revision conflict, rescan and persistence.");
    }
    private sealed class DirectProgress(Action<ArtistImportProgress> action):IProgress<ArtistImportProgress> { public void Report(ArtistImportProgress value)=>action(value); }
    private static async Task ExpectAsync<T>(Func<Task> action) where T:Exception
    { try { await action(); } catch(T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static void Check(bool valid,string message) { if(!valid) throw new InvalidOperationException(message); }
}
