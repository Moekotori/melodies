using System.Text.Json;
using Echo.Contracts.Settings;

namespace Echo.Infrastructure.Preferences;

public static class SettingsTransfer
{
    public static async Task<DesktopPreferences> ReadAsync(string path, CancellationToken token)
    {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
        if (input.Length > 524288) throw new InvalidDataException("设置文件超过 512 KiB。请选择 ECHO 导出的设置 JSON。");
        var settings = await JsonSerializer.DeserializeAsync(input, DesktopPreferencesJsonContext.Default.DesktopPreferences, token).ConfigureAwait(false);
        if (settings is null || settings.Version != 1) throw new InvalidDataException("设置版本不受支持。");
        return settings;
    }
    public static Task WriteAsync(string path, DesktopPreferences settings, CancellationToken token) => Task.Run(async () =>
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous))
                await JsonSerializer.SerializeAsync(output, settings, DesktopPreferencesJsonContext.Default.DesktopPreferences, token).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }, token);
}
