namespace Echo.Contracts.Settings;

public sealed record DesktopPreferences(int Version = 1, int UiScalePercent = 100, bool IsDark = false,
    bool SidebarIconOnly = false, bool HideSidebarInSettings = false, bool RememberWindowSize = true,
    double WindowWidth = 1440, double WindowHeight = 900, string MotionMode = "System", bool SidebarAutoHideEnabled = false,
    string Language = "system", ThemePreferences? Theme = null, LibraryViewPreferences[]? LibraryQueries = null,
    FloatingPreferences? Floating = null, Dictionary<string, FeatureSetting>? Features = null);

public interface IDesktopPreferencesStore : IAsyncDisposable
{
    Task<DesktopPreferences?> ReadAsync(CancellationToken cancellation);
    void Save(DesktopPreferences preferences);
    event Action<string>? Error;
}
