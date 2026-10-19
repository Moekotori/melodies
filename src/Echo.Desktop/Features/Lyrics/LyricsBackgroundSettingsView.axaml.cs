using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsBackgroundSettingsView:UserControl
{
    private bool attached;
    public LyricsBackgroundSettingsView()=>InitializeComponent();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e){base.OnAttachedToVisualTree(e);attached=true;}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){attached=false;base.OnDetachedFromVisualTree(e);}
    private async void OnPickWallpaper(object? sender,RoutedEventArgs e)
    {
        if(DataContext is not LyricsAppearance appearance||TopLevel.GetTopLevel(this) is not {} top)return;
        var files=await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {Title=appearance.WallpaperDialogTitle,AllowMultiple=false,FileTypeFilter=[FilePickerFileTypes.ImageAll]});
        if(attached&&ReferenceEquals(DataContext,appearance)&&files.Count>0&&files[0].TryGetLocalPath() is {} path)
        {appearance.WallpaperPath=path;appearance.BackgroundMode="customWallpaper";}
    }
}
