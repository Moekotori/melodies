using Echo.Desktop.Motion;

namespace Echo.Desktop.Shell;

public partial class MainWindow
{
    private string _shownRoute = "home";
    private int _pageRequest;

    // The outgoing scene is handed to the compositor before the destination is built, so the
    // render thread keeps it moving while the UI thread constructs the next page. A request
    // superseded during that wait builds nothing.
    private async void NavigatePage()
    {
        if (_viewModel is null || _surface is null) return;
        var request = ++_pageRequest;
        var route = _viewModel.ActiveRouteId;
        var direction = _viewModel.RouteDirection(_shownRoute, route);
        if (_shownRoute == "lyrics" && route != "lyrics") await LowerLyricsStage();
        else if (route != "lyrics") await PageHost.RetireAsync(direction);
        if (request != _pageRequest || _viewModel is null || _surface is null) return;
        ShowPage(direction);
    }
}
