using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace Echo.Wallpapers;

internal sealed class WallpaperBrowserProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _profile;
    private readonly Task _errors;
    private readonly CancellationTokenSource _stop = new();
    public int ProcessId => _process.Id;
    public WallpaperBrowserProcess(string binary, string cacheRoot)
    {
        _profile = Path.Combine(Path.GetFullPath(cacheRoot), "runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_profile);
        var info = new ProcessStartInfo(binary) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = false };
        foreach (var argument in new[] { "--headless=new", "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=0",
            "--user-data-dir=" + _profile, "--no-first-run", "--no-default-browser-check", "--disable-extensions", "--disable-sync",
            "--disable-background-networking", "--disable-component-update", "--disable-default-apps", "--disable-breakpad", "--disable-crash-reporter",
            "--disable-features=MediaRouter,OptimizationHints", "--autoplay-policy=no-user-gesture-required", "--mute-audio", "--hide-scrollbars",
            "--force-device-scale-factor=1", "--disk-cache-size=16777216", "--media-cache-size=8388608", "--js-flags=--max-old-space-size=192", "about:blank" })
            info.ArgumentList.Add(argument);
        _process = new Process { StartInfo = info };
        try
        {
            if (!_process.Start()) throw new IOException("动态壁纸渲染进程无法启动");
            _errors = Drain(_stop.Token);
        }
        catch { _process.Dispose(); Directory.Delete(_profile, true); _stop.Dispose(); throw; }
    }
    private async Task Drain(CancellationToken token)
    {
        var buffer = new char[2048];
        try { while (await _process.StandardError.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false) > 0) { } }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (IOException) { }
    }
    public async Task<Uri> PageEndpoint(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var portFile = Path.Combine(_profile, "DevToolsActivePort");
        string[] lines = [];
        while (lines.Length < 2)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (_process.HasExited) throw new IOException("动态壁纸渲染进程已退出");
            if (File.Exists(portFile))
            {
                try { lines = await File.ReadAllLinesAsync(portFile, timeout.Token).ConfigureAwait(false); }
                catch (IOException) { }
            }
            if (lines.Length < 2) await Task.Delay(40, timeout.Token).ConfigureAwait(false);
        }
        if (!int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is <= 0 or > 65535)
            throw new InvalidDataException("壁纸渲染连接无效");
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false });
        using var response = await client.GetAsync($"http://127.0.0.1:{port}/json/list", HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var bytes = new byte[65537]; var count = 0;
        while (count < bytes.Length)
        {
            var n = await stream.ReadAsync(bytes.AsMemory(count), timeout.Token).ConfigureAwait(false);
            if (n == 0) break; count += n;
        }
        if (count == bytes.Length) throw new InvalidDataException("渲染连接描述过大");
        using var document = JsonDocument.Parse(bytes.AsMemory(0, count));
        foreach (var item in document.RootElement.EnumerateArray())
            if (item.GetProperty("type").GetString() == "page" && item.TryGetProperty("webSocketDebuggerUrl", out var url))
            {
                var uri = new Uri(url.GetString()!);
                if (uri.Scheme != "ws" || uri.Port != port || !IPAddress.TryParse(uri.Host, out var ip) || !IPAddress.IsLoopback(ip))
                    throw new InvalidDataException("壁纸渲染连接不在本机");
                return uri;
            }
        throw new IOException("壁纸渲染页面不可用");
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
        finally
        {
            _stop.Cancel(); await _errors.ConfigureAwait(false); _process.Dispose(); _stop.Dispose();
            // Only this newly created UUID profile under cacheRoot is removed.
            try { Directory.Delete(_profile, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
