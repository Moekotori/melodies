using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Controls.Lyrics;

// Only visible picker rows own a small decoded source thumbnail. Opening the picker never starts a scene engine.
public sealed class LyricsBackgroundThumbnail:Control
{
    public static readonly StyledProperty<string> ModeProperty=AvaloniaProperty.Register<LyricsBackgroundThumbnail,string>(nameof(Mode),"page");
    public string Mode{get=>GetValue(ModeProperty);set=>SetValue(ModeProperty,value);}
    private Bitmap? image;
    private bool attached;
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e){base.OnAttachedToVisualTree(e);attached=true;Load();}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){attached=false;image?.Dispose();image=null;base.OnDetachedFromVisualTree(e);}
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e){base.OnPropertyChanged(e);if(e.Property==ModeProperty&&attached)Load();}
    private void Load()
    {
        image?.Dispose();image=null;
        var id=LyricsBackgroundCatalog.Resolve(Mode).Id;if(id=="page")id="auto";
        var uri=new Uri($"avares://Echo.Desktop/Resources/LyricsBackgrounds/{id}.webp");
        if(AssetLoader.Exists(uri)){using var stream=AssetLoader.Open(uri);image=Bitmap.DecodeToWidth(stream,128);}
        InvalidateVisual();
    }
    public override void Render(DrawingContext context)
    {
        if(image is null)return;
        var target=new Rect(Bounds.Size);
        using(context.PushClip(new RoundedRect(target,4)))LyricsStagePainter.Image(context,image,target);
    }
}
