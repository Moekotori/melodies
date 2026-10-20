namespace Echo.Desktop.Features.Lyrics;

// Window-owned current-track import choice. Two paths only; no library-wide map or document cache.
public sealed class LyricsSelection
{
    private string? track, importedPath;
    public event Action? ReloadRequested;
    public void RequestReload() => ReloadRequested?.Invoke();
    public string? ForTrack(string key) => track == key ? importedPath : null;
    public void Select(string key, string path) { track = key; importedPath = path; }
}
