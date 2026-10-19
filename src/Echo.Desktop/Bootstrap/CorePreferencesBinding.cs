using Echo.Core.Contracts;
using Echo.Desktop.Shell;
namespace Echo.Desktop.Bootstrap;

internal sealed class CorePreferencesBinding(ShellViewModel shell, ICoreSettings settings) : IDisposable
{
    private readonly CancellationTokenSource stop = new();
    private bool restoring, disposed;
    public async Task RestoreAsync()
    {
        var saved = await settings.ReadAsync(stop.Token);
        restoring = true;
        try { foreach (var pair in saved) shell.FeaturePreferences.Confirm(pair.Key, pair.Value); }
        finally { restoring = false; }
        shell.FeaturePreferences.Changed += Changed;
    }
    private void Changed(string id)
    {
        if (restoring || disposed) return;
        if (id.Length == 0)
        {
            foreach (var pair in shell.FeaturePreferences.Values.Where(pair => CoreFeatureKeys.Contains(pair.Key))) _ = SaveAsync(pair.Key, pair.Value);
            return;
        }
        if (!CoreFeatureKeys.Contains(id)) return;
        _ = SaveAsync(id, shell.FeaturePreferences.Read(id, new()));
    }
    private async Task SaveAsync(string id, Echo.Contracts.Settings.FeatureSetting value)
    {
        try { await settings.ConfirmAsync(id, value, stop.Token); }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!disposed) shell.Settings.Feedback = error.Message; }
    }
    public void Dispose() { disposed = true; shell.FeaturePreferences.Changed -= Changed; stop.Cancel(); }
}
