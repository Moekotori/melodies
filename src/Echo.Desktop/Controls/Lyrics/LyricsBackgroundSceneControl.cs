using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Echo.Desktop.Features.Lyrics;
using Echo.Desktop.Features.Player;
using Echo.Desktop.Motion;

namespace Echo.Desktop.Controls.Lyrics;

public sealed class LyricsBackgroundSceneControl:Control,IDisposable
{
    private LyricsViewModel? model;
    private MotionPolicy? motion;
    private AppearanceVisualsViewModel? visuals;
    private BackgroundSceneWorker? worker;
    private readonly LyricsFrameController frames;
    private SourceSceneFrame? frame;
    private SourceSceneReceipt? pending;
    private BackgroundSceneProject? project;
    private BackgroundSceneKey? key;
    private (bool Animate,bool Playing,int FrameRate)? renderPolicy;
    private IReadOnlyList<float>? sampledSpectrum;
    private float[] spectrumSnapshot=[];
    private long sampledTimestamp;
    private Avalonia.Media.Imaging.Bitmap? cover;
    private byte[]? coverBytes;
    private SourceSceneArtwork? artwork;
    private bool artworkPending;
    private long revision,lastRequest,frameRevision=-1;
    private int posted,width,height;
    private bool visible,closed,failed,focused=true;
    private bool? hostDark;
    public event Action<bool>? FallbackChanged;
    private int FrameRate=>Math.Max(1,Math.Min(focused?30:15,motion?.MaximumFrameRate??30));
    public long FrameSequence{get;private set;}
    public bool HasFrame=>frame is not null&&frameRevision==revision;
    public bool IsFrameDriverRunning=>frames.IsRunning;
    public string RenderDiagnostic{get;private set;}="";
    public LyricsBackgroundSceneControl()
    {
        ClipToBounds=true;IsVisible=false;
        frames=new(()=>RequestFrame(false),()=>visible&&!failed&&motion?.CanAnimate==true&&model?.Appearance.BackgroundMode!="customWallpaper",()=>FrameRate);
        SizeChanged+=(_,_)=>Changed();ActualThemeVariantChanged+=(_,_)=>Changed();DetachedFromVisualTree+=(_,_)=>Dispose();
    }
    public void Bind(LyricsViewModel value,MotionPolicy policy,AppearanceVisualsViewModel visualState)
    {
        model=value;motion=policy;visuals=visualState;
        model.Changed+=Changed;model.PropertyChanged+=ModelChanged;model.Appearance.PropertyChanged+=AppearanceChanged;
        motion.PropertyChanged+=AppearanceChanged;Changed();
    }
    private void ModelChanged(object? sender,PropertyChangedEventArgs e)
    {if(e.PropertyName is nameof(LyricsViewModel.Cover) or nameof(LyricsViewModel.CoverColors))Changed();}
    private void AppearanceChanged(object? sender,PropertyChangedEventArgs e)=>Changed();
    private void Changed()
    {
        if(closed||model is null||motion is null)return;
        var enabled=(model.Appearance.BackgroundStyle.Scene||model.Appearance.IsCustomWallpaper)&&model.Appearance.SelectedStyle.Layout is not (LyricsLayout.SourceScene or LyricsLayout.Afterglow);
        visible=enabled&&motion.IsWindowVisible;IsVisible=visible&&!failed;
        if(visuals is not null)visuals.LyricsSpectrum=visible&&motion.CanAnimate&&model.Appearance.BackgroundMode is "flowingLight" or "geometricAmbience";
        if(!visible)
        {
            frames.Update(false,false);FallbackChanged?.Invoke(false);
            if(key is not null||frame is not null)
            {revision++;key=null;project=null;cover=null;coverBytes=null;renderPolicy=null;sampledSpectrum=null;spectrumSnapshot=[];sampledTimestamp=0;artworkPending=false;artwork?.Show(null);worker?.Request(new(revision,null,0,0,false,false,[]));frame?.Dispose();frame=null;Interlocked.Exchange(ref pending,null)?.Frame?.Dispose();InvalidateVisual();}
            return;
        }
        if(Bounds.Width<2||Bounds.Height<2)return;
        var scale=Math.Min(1,Math.Min(8192/Bounds.Width,8192/Bounds.Height));width=(int)(Bounds.Width*scale);height=(int)(Bounds.Height*scale);
        var dark=hostDark??ActualThemeVariant==ThemeVariant.Dark;
        var sceneCover=model.Appearance.BackgroundMode.StartsWith("cover",StringComparison.Ordinal)?model.Cover:null;
        var next=new BackgroundSceneKey(model.Appearance.BackgroundMode,dark,model.Player.CurrentFilePath,sceneCover,model.CoverColors,width,height,model.Appearance.WallpaperPath,model.Appearance.BackgroundBlur,model.Appearance.BackgroundBrightness,model.Appearance.BackgroundOpacity);
        var changed=key!=next;
        var reactive=model.Appearance.BackgroundMode is "flowingLight" or "geometricAmbience";
        var nextPolicy=(model.Appearance.BackgroundStyle.Scene&&motion.UseSpatialMotion,reactive&&model.IsPlaying,model.Appearance.BackgroundStyle.Scene?FrameRate:0);
        var policyChanged=renderPolicy is null||renderPolicy.Value!=nextPolicy;renderPolicy=nextPolicy;
        if(changed)
        {
            key=next;revision++;failed=false;FallbackChanged?.Invoke(false);IsVisible=true;RenderDiagnostic="";
            if(!ReferenceEquals(cover,sceneCover))
            {
                cover=sceneCover;coverBytes=null;artworkPending=cover is not null;
                if(artworkPending){artwork??=new(ArtworkReady);var lease=model.RetainCover();if(lease is null)artworkPending=false;artwork.Show(lease);}
                else artwork?.Show(null);
            }
            project=BackgroundSceneProjectBuilder.Create(model,dark,coverBytes);
        }
        if(changed||policyChanged)RequestFrame(true);frames.Update(true,focused);
    }
    private void ArtworkReady(byte[]? bytes)
    {
        coverBytes=bytes;artworkPending=false;
        if(project is not null)project=project with { Cover=bytes };
        RequestFrame(true);
    }
    private void RequestFrame(bool force,double pointerX=-1,double pointerY=-1,bool press=false,bool leave=false)
    {
        if(!visible||failed||closed||artworkPending||model is null||motion is null||project is null)return;
        if(!force&&Stopwatch.GetElapsedTime(lastRequest).TotalMilliseconds<1000d/FrameRate)return;
        lastRequest=Stopwatch.GetTimestamp();worker??=new(Receive);
        var reactive=model.Appearance.BackgroundMode is "flowingLight" or "geometricAmbience";
        var source=reactive?visuals?.LiveSpectrum:null;
        var timestamp=reactive?visuals?.LiveSpectrumTimestamp??0:0;
        if(!ReferenceEquals(sampledSpectrum,source)||sampledTimestamp!=timestamp)
        {
            sampledSpectrum=source;sampledTimestamp=timestamp;
            var count=Math.Min(32,source?.Count??0);spectrumSnapshot=count==0?[]:new float[count];
            for(var i=0;i<count;i++)spectrumSnapshot[i]=source![i];
        }
        worker.Request(new(revision,project,width,height,motion.UseSpatialMotion,reactive&&model.IsPlaying,spectrumSnapshot,pointerX,pointerY,press,leave,timestamp,FrameRate));
    }
    private void Receive(SourceSceneReceipt value)
    {
        if(Volatile.Read(ref closed)){value.Frame?.Dispose();return;}
        Interlocked.Exchange(ref pending,value)?.Frame?.Dispose();
        if(Volatile.Read(ref closed)){Interlocked.Exchange(ref pending,null)?.Frame?.Dispose();return;}
        if(Interlocked.Exchange(ref posted,1)!=0)return;
        Dispatcher.UIThread.Post(()=>
        {
            Interlocked.Exchange(ref posted,0);var receipt=Interlocked.Exchange(ref pending,null);if(receipt is null)return;
            if(closed||!visible||receipt.Revision!=revision){receipt.Frame?.Dispose();return;}
            frame?.Dispose();frame=receipt.Frame;frameRevision=receipt.Revision;if(frame is not null)FrameSequence++;RenderDiagnostic=receipt.Diagnostic??"";
            if(receipt.Error is not null){failed=true;frames.Update(false,false);IsVisible=false;FallbackChanged?.Invoke(true);}
            InvalidateVisual();
        });
    }
    public override void Render(Avalonia.Media.DrawingContext context)
    {if(frame is not null)context.Custom(new SourceSceneDraw(frame,new Rect(Bounds.Size)));}
    public void Pointer(Point point,bool press)
    {
        if(model is null||!LyricsBackgroundInk.IsDarkSource(model.Appearance.BackgroundMode)||Bounds.Width<=0||Bounds.Height<=0)return;
        if(point.X<0||point.Y<0||point.X>Bounds.Width||point.Y>Bounds.Height){PointerLeave();return;}
        RequestFrame(press,point.X/Bounds.Width,point.Y/Bounds.Height,press);
    }
    public void PointerLeave(){if(model is not null&&LyricsBackgroundInk.IsDarkSource(model.Appearance.BackgroundMode))RequestFrame(true,leave:true);}
    public void SetDark(bool dark){hostDark=dark;Changed();}
    public void SetFocused(bool value){focused=value;frames.Update(visible,value);}
    public void Dispose()
    {
        if(closed)return;closed=true;revision++;frames.Dispose();worker?.Dispose();artwork?.Dispose();frame?.Dispose();Interlocked.Exchange(ref pending,null)?.Frame?.Dispose();
        if(model is not null){model.Changed-=Changed;model.PropertyChanged-=ModelChanged;model.Appearance.PropertyChanged-=AppearanceChanged;}
        if(motion is not null)motion.PropertyChanged-=AppearanceChanged;
        if(visuals is not null)visuals.LyricsSpectrum=false;
        model=null;motion=null;visuals=null;cover=null;coverBytes=null;project=null;frame=null;key=null;sampledSpectrum=null;spectrumSnapshot=[];renderPolicy=null;FallbackChanged=null;
    }
}
