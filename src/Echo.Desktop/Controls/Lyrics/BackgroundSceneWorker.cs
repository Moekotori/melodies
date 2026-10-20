using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Echo.Visuals;

namespace Echo.Desktop.Controls.Lyrics;

internal sealed record BackgroundSceneRequest(long Revision,BackgroundSceneProject? Project,int Width,int Height,
    bool Animate,bool Playing,float[] Spectrum,double PointerX=-1,double PointerY=-1,bool Press=false,bool Leave=false,long SpectrumTimestamp=0,int FrameRate=60,long Activity=0);
internal sealed record BackgroundSceneReceipt(long Revision,SourceSceneFrame? Frame,string? Error,string? Diagnostic=null,bool Idle=false,long Activity=0);
internal sealed class BackgroundSceneWorker:IDisposable
{
    private readonly Channel<BackgroundSceneRequest> requests=Channel.CreateBounded<BackgroundSceneRequest>(new BoundedChannelOptions(1){SingleReader=true,FullMode=BoundedChannelFullMode.DropOldest});
    private readonly CancellationTokenSource stop=new();
    private readonly Action<BackgroundSceneReceipt> deliver;
    private readonly Task running;
    private BackgroundSceneRequest? pendingPress;
    public BackgroundSceneWorker(Action<BackgroundSceneReceipt> deliver){this.deliver=deliver;running=Task.Run(Run);}
    public void Request(BackgroundSceneRequest value){if(value.Press)Interlocked.Exchange(ref pendingPress,value);requests.Writer.TryWrite(value);}
    private async Task Run()
    {
        SourceBackgroundEngine? engine=null;BackgroundWallpaperScene? wallpaper=null;BackgroundWallpaperAsset? wallpaperAsset=null;long revision=-1;bool failed=false;
        BackgroundSceneProject? previousProject=null;float[]? lastSpectrum=null;var spectrumJson="[]";
        var clock=Stopwatch.StartNew();double previous=0;
        try
        {
            await foreach(var request in requests.Reader.ReadAllAsync(stop.Token))
            {
                if(request.Project is null){engine?.Dispose();engine=null;wallpaper?.Dispose();wallpaper=null;wallpaperAsset?.Dispose();wallpaperAsset=null;previousProject=null;lastSpectrum=null;spectrumJson="[]";revision=-1;continue;}
                if(failed&&revision==request.Revision)continue;
                try
                {
                    if(revision!=request.Revision)
                    {
                        wallpaper?.Dispose();wallpaper=null;revision=request.Revision;failed=false;
                        if(request.Project.Mode=="customWallpaper")
                        {
                            engine?.Dispose();engine=null;
                            if(wallpaperAsset?.Path!=request.Project.WallpaperPath){wallpaperAsset?.Dispose();wallpaperAsset=null;if(request.Project.WallpaperPath.Length>0)wallpaperAsset=new(request.Project.WallpaperPath);}
                            wallpaper=new(request.Project,request.Width,request.Height,wallpaperAsset);
                        }
                        else
                        {
                            wallpaperAsset?.Dispose();wallpaperAsset=null;
                            var json=JsonSerializer.Serialize(request.Project,BackgroundSceneJson.Default.BackgroundSceneProject);
                            var retained=engine is not null&&previousProject is not null&&CanRetain(previousProject.Mode,request.Project.Mode);
                            if(retained)engine!.Update(json,request.Width,request.Height);
                            else {engine?.Dispose();engine=new(stop.Token);engine.Build(json,request.Width,request.Height);previous=clock.Elapsed.TotalMilliseconds;}
                            if(!retained||!ReferenceEquals(previousProject?.Cover,request.Project.Cover))
                            {
                                if(request.Project.Cover is {} cover)engine!.SetCover(cover);
                                else engine!.ClearCover();
                            }
                        }
                        previousProject=request.Project;
                    }
                    var press=Interlocked.Exchange(ref pendingPress,null);var pointer=press?.Revision==revision?press:request;
                    if(pointer.PointerX>=0)engine?.Pointer(pointer.PointerX,pointer.PointerY,pointer.Press);
                    if(request.Leave)engine?.PointerLeave();
                    var now=clock.Elapsed.TotalMilliseconds;
                    if(!ReferenceEquals(lastSpectrum,request.Spectrum))
                    {lastSpectrum=request.Spectrum;spectrumJson=JsonSerializer.Serialize(request.Spectrum,BackgroundSceneJson.Default.SingleArray);}
                    var picture=wallpaper?.Draw()??engine!.TryDraw(now,request.Animate?Math.Clamp(now-previous,1,250):0,
                        spectrumJson,request.Playing,request.SpectrumTimestamp,request.FrameRate);
                    previous=now;
                    if(picture is null){deliver(new(revision,null,null,Idle:true,Activity:request.Activity));continue;}
                    if(stop.IsCancellationRequested){picture.Dispose();break;}
                    // Engine/wallpaper returns an owned immutable picture; transfer it directly to the frame.
                    deliver(new(revision,new(picture,(int)picture.CullRect.Width,(int)picture.CullRect.Height),null,Activity:request.Activity));
                }
                catch(OperationCanceledException)when(stop.IsCancellationRequested){break;}
                catch(Exception error){failed=true;engine?.Dispose();engine=null;wallpaper?.Dispose();wallpaper=null;wallpaperAsset?.Dispose();wallpaperAsset=null;deliver(new(revision,null,"背景绘制失败",error.ToString()));}
            }
        }
        catch(OperationCanceledException)when(stop.IsCancellationRequested){}
        finally{engine?.Dispose();wallpaper?.Dispose();wallpaperAsset?.Dispose();}
    }
    private static bool CanRetain(string previous,string next)=>previous==next||(Shader(previous)&&Shader(next));
    private static bool Shader(string mode)=>mode is not ("coverFlow" or "flowingLight" or "geometricAmbience" or "customWallpaper");
    public void Dispose(){stop.Cancel();requests.Writer.TryComplete();_=running.ContinueWith(_=>stop.Dispose(),TaskScheduler.Default);}
}
