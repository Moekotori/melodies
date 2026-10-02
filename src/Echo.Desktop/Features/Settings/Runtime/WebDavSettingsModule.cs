using Echo.Infrastructure.Backup;
using Echo.Contracts.Library;
using Echo.Desktop.Shell;

namespace Echo.Desktop.Features.Settings.Runtime;

public sealed class WebDavSettingsModule : IDisposable
{
    private readonly WebDavBackupClient _client = new();
    public WebDavSettingsModule(SettingsFeatureRuntime runtime, SettingsDialogs dialogs, ShellViewModel shell, string directory)
    {
        var credentials = new ProtectedCredentials(Path.Combine(directory, "credentials"));
        runtime.RegisterAction("webdav-backup", async (_, token) =>
        {
            var saved = await credentials.ReadAsync("webdav", token);
            var fields = await dialogs.EditAsync("WebDAV 网盘备份", "配置只保存在本机。上传会包含曲库路径和应用偏好，请使用自己的专用备份目录。",
                new("url", "HTTPS 备份目录", saved.GetValueOrDefault("url", "")), new("user", "用户名", saved.GetValueOrDefault("user", "")),
                new("password", "密码 / 应用密码", saved.GetValueOrDefault("password", ""), true),
                new("operation", "操作", "测试并保存", Choices: ["测试并保存", "立即备份到网盘"]));
            var uri = new Uri(fields["url"]); await _client.TestAsync(uri, fields["user"], fields["password"], token);
            await credentials.SaveAsync("webdav", fields, token);
            if (fields["operation"] == "立即备份到网盘")
            {
                var archive = Path.Combine(directory, "webdav-upload.zip");
                await new LocalBackupService(directory, shell.AlbumLibrary as ILibraryMaintenance).CreateAsync(archive, token);
                await _client.UploadAsync(uri, fields["user"], fields["password"], archive, token);
                shell.Settings.Feedback = "网盘备份已由服务端确认。";
            }
            else shell.Settings.Feedback = "WebDAV 连接已验证，配置已在本机保护保存。";
        });
    }
    public void Dispose() => _client.Dispose();
}
