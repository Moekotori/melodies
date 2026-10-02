using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Lyrics;

public partial class LyricsView : UserControl, IDisposable
{
    private readonly ShellViewModel shell;
    private readonly string backRoute;
    private bool updatingScroll, closed;
    public LyricsViewModel Model { get; }
    public LyricsView(ShellViewModel shell, string backRoute = "home")
    {
        this.shell = shell; this.backRoute = backRoute;
        Model = new(shell.Player, shell.LyricsSource!, shell.LyricsAppearance, shell.AlbumArtwork, shell.LyricsSelection);
        InitializeComponent(); DataContext = Model;
        Transport.Bind(shell); LyricsQueueOverlay.DataContext = shell;
        Viewport.Bind(Model, shell.Motion);
        Backdrop.Bind(Model);
        Viewport.FrameRequested += Backdrop.UpdateClock;
        Viewport.BrowseChanged += UpdateScroll;
        Model.Changed += OnChanged;
        InitializeChrome();
        DetachedFromVisualTree += (_, _) => Dispose();
        UpdateScroll();
    }
    private void OnChanged() { if (closed) return; UpdateScroll(); }
    private void UpdateScroll()
    {
        updatingScroll = true;
        LineScroll.Maximum = Math.Max(0, Model.Document.Lines.Count - 1);
        LineScroll.ViewportSize = Math.Min(5, Model.Document.Lines.Count);
        LineScroll.IsVisible = Model.Document.Lines.Count > 5;
        LineScroll.Value = Viewport.DisplayIndex;
        FollowButton.IsVisible = !Viewport.IsFollowing;
        updatingScroll = false;
    }
    private void OnScroll(object? sender, RangeBaseValueChangedEventArgs e)
    { if (!updatingScroll && Model is not null) Viewport.BrowseTo((int)e.NewValue); }
    private void OnFollow(object? sender, RoutedEventArgs e) => Viewport.Follow();
    private void OnBack(object? sender, RoutedEventArgs e) => shell.NavigateCommand.Execute(backRoute);
    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (closed || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            var files = await top.StorageProvider.OpenFilePickerAsync(new()
            {
                Title = shell.Text.Translate("导入歌词"), AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(shell.Text.Translate("歌词文件")) { Patterns = ["*.lrc", "*.yrc", "*.ttml", "*.srt", "*.vtt", "*.txt"] }]
            });
            if (!closed && files.Count > 0 && files[0].TryGetLocalPath() is { } path) await Model.ImportAsync(path);
        }
        catch (Exception) { if (!closed) Model.Message = shell.Text.Translate("无法导入歌词，请重新选择文件。"); }
    }
    public void SetFocused(bool focused) => Viewport.SetFocused(focused);
    public void Dispose()
    {
        if (closed) return; closed = true;
        DisposeChrome();
        Viewport.FrameRequested -= Backdrop.UpdateClock;
        Viewport.BrowseChanged -= UpdateScroll; Model.Changed -= OnChanged; Viewport.Dispose(); Backdrop.Dispose(); Model.Dispose();
    }
}
