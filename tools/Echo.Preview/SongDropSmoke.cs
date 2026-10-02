using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Echo.Contracts.Scanning;
using Echo.Desktop.Features.Albums;
using Echo.Desktop.Motion;
using Echo.Desktop.Shell;
using Echo.Infrastructure.Artwork;
using Echo.Infrastructure.Library;

internal static class SongDropSmoke
{
    public static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var opening = LibraryDatabase.OpenAsync(Path.Combine(output, Guid.NewGuid().ToString("N") + ".sqlite"));
        Pump(() => opening.IsCompleted); var db = opening.GetAwaiter().GetResult();
        var artwork = new AlbumArtworkCache(new LocalAlbumArtwork());
        var shell = new ShellViewModel(); shell.Text.SetLanguage("zh-CN");
        shell.Motion.SelectedChoice = shell.Motion.Choices.First(choice => choice.Mode == MotionMode.Off);
        var calls = new List<string>();
        var complete = new TaskCompletionSource<ScanReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        shell.AttachLibrary(db, artwork, async (path, progress, token) =>
        {
            calls.Add(path);
            progress.Report(new("drop", "processing", 1, 1, 1, 0, 0, 0));
            return await complete.Task.WaitAsync(token);
        });
        var window = new MainWindow { DataContext = shell };
        using var files = new DataTransfer();
        var song = new DropFile(Path.Combine(output, "歌曲.wav"));
        files.Add(DataTransferItem.CreateFile(song));
        files.Add(DataTransferItem.CreateFile(song));
        files.Add(DataTransferItem.CreateFile(new DropFile(Path.Combine(output, "notes.txt"))));
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            window.DragDrop(new Point(600, 350), RawDragEventType.DragOver, files, DragDropEffects.Copy);
            Check(shell.IsSongDropHover, "OS file drag did not reveal the import hint.");
            Capture(window, Path.Combine(output, "drop-light.png"));
            shell.IsDark = true; Dispatcher.UIThread.RunJobs();
            Capture(window, Path.Combine(output, "drop-dark.png"));
            window.DragDrop(new Point(600, 350), RawDragEventType.Drop, files, DragDropEffects.Copy);
            Pump(() => shell.IsSongImportActive && calls.Count == 1 && shell.SongImportStatus.Contains("已处理 1"));
            Check(!shell.IsSongDropHover && calls[0] == song.TryGetLocalPath(), "Drop path was lost.");
            window.DragDrop(new Point(600, 350), RawDragEventType.Drop, files, DragDropEffects.Copy);
            Check(calls.Count == 1, "Busy drop launched a second scan.");
            shell.NavigateCommand.Execute("songs"); Dispatcher.UIThread.RunJobs();
            Check(shell.IsSongImportActive, "Navigation cancelled the window-owned import.");
            Capture(window, Path.Combine(output, "import-progress.png"));
            complete.SetResult(new("drop", ScanStatus.Completed, 1, 1, 0, 0, 0, 0, 1, null));
            Pump(() => !shell.IsSongImportActive);
            Check(calls.Count == 1 && shell.Notice.Contains("跳过 1"), "Duplicate/unsupported drop handling failed.");
            complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
            window.DragDrop(new Point(600, 350), RawDragEventType.Drop, files, DragDropEffects.Copy);
            Pump(() => shell.IsSongImportActive && calls.Count == 2);
            shell.CancelSongImportCommand.Execute(null); Pump(() => !shell.IsSongImportActive);
            Check(shell.Notice.Contains("取消"), "Cancellation was not visible.");
            using var text = new DataTransfer(); text.Add(DataTransferItem.CreateText("hello"));
            window.DragDrop(new Point(600, 350), RawDragEventType.DragOver, text, DragDropEffects.Copy);
            Check(!shell.IsSongDropHover && calls.Count == 2, "Text drag was accepted.");
            Console.WriteLine("Song drop: actual Avalonia drag events, dedup/filter, busy admission, navigation, completion and cancellation passed.");
        }
        finally
        {
            window.RequestExit(); shell.ReleaseLocalization();
            var release = artwork.DisposeAsync().AsTask(); Pump(() => release.IsCompleted); release.GetAwaiter().GetResult();
            var close = db.DisposeAsync().AsTask(); Pump(() => close.IsCompleted); close.GetAwaiter().GetResult();
        }
    }
    private static void Capture(Window window, string path)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = new RenderTargetBitmap(new PixelSize(1440, 900), new Vector(96, 96));
        bitmap.Render(window); bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
    private static void Pump(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready()) { if (timer.Elapsed.TotalSeconds > 10) throw new TimeoutException(); Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Dispatcher.UIThread.RunJobs();
    }
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }

    // Storage methods deliberately fail: drag filtering must only read the name and local URI.
    private sealed class DropFile(string path) : IStorageFile
    {
        public string Name => System.IO.Path.GetFileName(path);
        public Uri Path => new(path);
        public bool CanBookmark => false;
        public void Dispose() { }
        public Task<StorageItemProperties> GetBasicPropertiesAsync() => throw new NotSupportedException();
        public Task<string?> SaveBookmarkAsync() => throw new NotSupportedException();
        public Task<IStorageFolder?> GetParentAsync() => throw new NotSupportedException();
        public Task DeleteAsync() => throw new NotSupportedException();
        public Task<IStorageItem?> MoveAsync(IStorageFolder destination) => throw new NotSupportedException();
        public Task<Stream> OpenReadAsync() => throw new NotSupportedException();
        public Task<Stream> OpenWriteAsync() => throw new NotSupportedException();
    }
}
