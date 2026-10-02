using Avalonia.Controls;
using Echo.Desktop.Features.Lyrics;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private WindowState? beforeLyrics;
    private bool ShowStandaloneLyrics()
    {
        if (_viewModel?.ActiveRouteId == "lyrics" && _viewModel.LyricsSource is not null)
        {
            beforeLyrics ??= WindowState;
            ReleaseLibraryPage(); ReleaseLyricsPage();
            PageHost.Content = null; PageHost.Settle();
            _viewModel.CloseQueueDrawerCommand.Execute(null);
            UiScaleHost.IsVisible = false; LyricsStageHost.IsVisible = true;
            _lyricsPage = new LyricsView(_viewModel, _previousRoute);
            LyricsStageHost.Content = _lyricsPage;
            return true;
        }
        if (LyricsStageHost.IsVisible)
        {
            ReleaseLyricsPage(); LyricsStageHost.Content = null;
            LyricsStageHost.IsVisible = false; UiScaleHost.IsVisible = true;
            if (beforeLyrics is { } previous) WindowState = previous;
            beforeLyrics = null;
        }
        return false;
    }
}
