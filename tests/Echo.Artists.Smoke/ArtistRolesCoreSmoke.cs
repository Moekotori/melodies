using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Echo.Application.Playback;
using Echo.Contracts.Library;
using Echo.Contracts.Scanning;
using Echo.Core;
using Echo.Core.Client;
using Echo.Core.Contracts;
using Echo.Core.Hosting;
using Echo.Core.Transport;
using Echo.Desktop.Features.Artists;
using Echo.Desktop.Localization;
using Echo.Desktop.Theming;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Library;

internal static class ArtistRolesCoreSmoke
{
    public static async Task PrepareAsync(string root)
    {
        Directory.CreateDirectory(root);
        await using (var db=await LibraryDatabase.OpenAsync(Path.Combine(root,"roles.sqlite")))
        {
            var job=await db.BeginScanAsync(root,false,CancellationToken.None);
            var items=Enumerable.Range(0,250).Select(index=>new ScanEntry(
                new(Path.Combine(root,index+".flac"),LibraryPath.Key(Path.Combine(root,index+".flac")),100,100),ScanEntryKind.Metadata,
                new("Blue Hour "+index,"Aurora::Björk","Northern Lights","Aurora::Björk",60,"FLAC",48000,24,
                    Credits:[new("ARTIST","Aurora::Björk"),new("COMPOSER","久石让"),new("PERSONNEL","Maya Chen - Flute"),new("PERSONNEL","Maya Chen - Producer")]))).ToArray();
            await db.CommitScanBatchAsync(job,1,items,CancellationToken.None);
            await db.FinishScanAsync(job,ScanStatus.Completed,250,false,null,CancellationToken.None);
        }
        await using var fixture=new Host(root);
        var first=await CoreConnection.ConnectAsync(fixture.Endpoint,token:fixture.Token);
        await using var second=await CoreConnection.ConnectAsync(fixture.Endpoint,token:fixture.Token);
        var notice=new TaskCompletionSource<ArtistImportSettings>(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IArtistImportNotifications)second.Library).ArtistImportChanged += value=>notice.TrySetResult(value);
        var tasks=(ICoreTaskControl)first.Library;
        var request=new CoreTaskRequest("",Id:Guid.NewGuid(),ArtistImport:new(0,["::"]));
        var accepted=await tasks.StartTaskAsync(request,fixture.Token);
        await first.DisposeAsync();
        var other=(ICoreTaskControl)second.Library;
        CoreTaskSnapshot state;
        do { state=await other.GetTaskAsync(accepted.Id,fixture.Token); if(state.Status=="running") await Task.Delay(10,fixture.Token); } while(state.Status=="running");
        Check(state.Status=="completed" && state.ArtistImportProgress?.Processed==250,"Accepted task survives Control disconnect.");
        Check((await notice.Task.WaitAsync(fixture.Token)).Revision==1,"Other Controls receive a bounded settings invalidation after commit.");
        Check((await second.Library.GetArtistsAsync(new(),fixture.Token)).Items.Count==2,"Custom policy reaches primary artist RPC.");
        Check((await second.Library.GetArtistImportSettingsAsync(fixture.Token)).Revision==1,"Settings persist under one Core owner.");
        Check((await other.StartTaskAsync(request,fixture.Token)).Id==accepted.Id,"Operation receipt prevents replay.");
        await using var reconnected=await CoreConnection.ConnectAsync(fixture.Endpoint,token:fixture.Token);
        var people=await reconnected.Library.GetPeopleAsync(new(Category:CreditCategory.Composer),fixture.Token);
        Check(people.Items.Single().Name=="久石让","New Control reads persisted role state.");
        var appearances=await reconnected.Library.GetPersonCreditsAsync(new(people.Items[0].Id,Limit:1),fixture.Token);
        Check((await second.Library.GetTrackCreditsAsync(appearances.Items[0].Track.Id,cancellation:fixture.Token)).Items.Any(item=>item.Name=="Maya Chen" && item.Category==CreditCategory.Production),"Bidirectional role DTOs cross production IPC.");
        Console.WriteLine("PASS credit Core IPC: static DTOs, task receipt, disconnect/reconnect, independent Controls, persisted configuration and role navigation.");
    }
    public static void Render(string root)
    {
        var fixture=new Host(root);
        var connection=CoreConnection.ConnectAsync(fixture.Endpoint,token:fixture.Token).GetAwaiter().GetResult();
        var view=new ArtistCreditsView(connection.Library,(ICoreTaskControl)connection.Library);
        var window=new Window { Content=new Border { Padding=new(24),Child=view },Width=1100,Height=900 };
        var text=new LocalizationService(); text.SetLanguage("zh-CN");
        using var localization=new WindowLocalizationBinding(window,text);
        var theme=new ThemeViewModel();
        foreach(var dark in new[]{false,true}) window.Resources.ThemeDictionaries[dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light]
            =ThemeResourcePalette.Create(theme.Tone(dark),theme.Snapshot,dark,null);
        window.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;
        try
        {
            window.Show(); Until(()=>view.Model.People.Count==4);
            view.Model.SelectedPerson=view.Model.People.Single(person=>person.Name=="Maya Chen");
            Until(()=>view.Model.Appearances.Count==100);
            view.Model.SelectedAppearance=view.Model.Appearances[0]; Until(()=>view.Model.TrackCredits.Count==7);
            Capture("roles-dark.png");
            window.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light; window.Width=760; Capture("roles-light-narrow.png");
            var expander=view.GetVisualDescendants().OfType<Expander>().Single(); expander.IsExpanded=true;
            Until(()=>view.GetVisualDescendants().OfType<ArtistImportView>().Any());
            var editor=(ArtistImportViewModel)view.GetVisualDescendants().OfType<ArtistImportView>().Single().DataContext!;
            Until(()=>editor.Delimiters=="::"); editor.Delimiters=";\n::";
            var saved=editor.SaveCommand.ExecuteAsync(null); Until(()=>saved.IsCompleted); saved.GetAwaiter().GetResult();
            Check(editor.Status=="已应用","Editor applies a real Core task.");
            Until(()=>view.Model.People.Count==4); view.Model.SelectedPerson=view.Model.People.Single(person=>person.Name=="Maya Chen");
            Until(()=>view.Model.Appearances.Count==100);
            Capture("import-settings-light.png");
            expander.IsExpanded=false; expander.IsExpanded=true; Settle();
            var reset=editor.ResetCommand.ExecuteAsync(null); Until(()=>reset.IsCompleted); reset.GetAwaiter().GetResult();
            Check(editor.Status=="已应用" && editor.Delimiters==";","Collapsed editor can reopen and reset persisted defaults.");
            Console.WriteLine("PASS credit UI: actual Avalonia layout, 100-row pages, person → appearances → credits, Core-backed apply/reset, light/dark and 760px snapshots.");
        }
        finally { window.Close(); view.Dispose(); connection.DisposeAsync().AsTask().GetAwaiter().GetResult(); fixture.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        void Capture(string name) { Settle(); using var image=window.CaptureRenderedFrame() ?? throw new IOException("Missing role frame."); image.Save(Path.Combine(root,name),PngBitmapEncoderOptions.Default); }
    }
    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private static void Until(Func<bool> ready)
    { var timer=Stopwatch.StartNew(); while(!ready()) { if(timer.Elapsed.TotalSeconds>10) throw new TimeoutException("Credit UI state."); Settle(); Thread.Sleep(2); } Settle(); }
    private static void Check(bool valid,string message) { if(!valid) throw new InvalidOperationException(message); }
    private sealed class Host:IAsyncDisposable
    {
        private readonly CancellationTokenSource stop=new(TimeSpan.FromSeconds(45));
        private readonly CoreRuntime runtime;
        private readonly Task work;
        public CoreEndpoint Endpoint { get; }
        public CancellationToken Token=>stop.Token;
        public Host(string root)
        { Endpoint=new(root,Path.Combine(root,"roles.sqlite")); runtime=new(new PlaybackRuntime(new ArtistDetailSmoke.AudioFixture()),Endpoint.LibraryPath); work=CoreServiceHost.RunAsync(runtime,Endpoint,TimeSpan.FromMinutes(1),stop.Token); }
        public async ValueTask DisposeAsync() { stop.Cancel(); await work.ConfigureAwait(false); await runtime.DisposeAsync().ConfigureAwait(false); stop.Dispose(); }
    }
}
