using CommunityToolkit.Mvvm.ComponentModel;
using Echo.Application.Playback;
using Echo.Desktop.Features.Settings.Runtime;

namespace Echo.Desktop.Features.Player;

public sealed partial class PlayerViewModel
{
    [ObservableProperty] private PlayerToolsViewModel? tools;
    public void AttachTools(SleepTimerService timer, FeaturePreferences preferences)
    {
        Tools?.Dispose();
        Tools = new(session, timer, preferences, Text);
    }
}
