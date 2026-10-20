using System.Threading.Channels;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Threading;
using Echo.Contracts.Settings;
using Echo.Desktop.Theming;
using Echo.Infrastructure.Preferences;
using SkiaSharp;

namespace Echo.Desktop.Bootstrap;

// The window owns one registered collection with at most four active files and 64 MiB font bytes.
internal sealed class ThemeFontBinding : IDisposable
{
    private sealed class Collection : FontCollectionBase { public override Uri Key { get; } = new("fonts:echo-import/" + Guid.NewGuid().ToString("N")); }
    private readonly ThemeViewModel _theme;
    private readonly Action _refresh;
    private readonly ImportedFontStore _store = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO Avalonia", "fonts"));
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<AppearancePreferences> _requests = Channel.CreateBounded<AppearancePreferences>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
    private Collection? _collection;
    private AppearancePreferences? _requested;
    private AppearancePreferences? _loaded;
    private bool _disposed;
    private bool _importing;
    public ThemeFontBinding(ThemeViewModel theme, Action refresh)
    {
        _theme = theme; _refresh = refresh; theme.ImportFontAsync = ImportAsync;
        _ = Task.Run(Work);
    }
    public void Apply(AppearancePreferences appearance)
    {
        if (_requested?.MainFontFile == appearance.MainFontFile && _requested?.ChineseFontFile == appearance.ChineseFontFile && _requested?.FallbackFontFile == appearance.FallbackFontFile
            && _requested?.Typography?.SecondaryFontFile == appearance.Typography?.SecondaryFontFile) return;
        _requested = appearance; _requests.Writer.TryWrite(appearance);
    }
    public FontFamily Family(ThemePreferences p) => Chain(p, p.Font, p.Appearance?.MainFontFile, _loaded?.MainFontFile);
    // An empty secondary font follows the main font; both share the Chinese and fallback slots.
    public FontFamily SecondaryFamily(ThemePreferences p) => p.Appearance?.Typography is { SecondaryFont.Length: > 0 } type
        ? Chain(p, type.SecondaryFont, type.SecondaryFontFile, _loaded?.Typography?.SecondaryFontFile) : Family(p);
    private string Slot(string name, ImportedFont? file, ImportedFont? loaded) => _collection is not null && file is not null && file == loaded && name == file.Family
        ? _collection.Key.AbsoluteUri + "#" + file.Family : name == "Outfit" ? "avares://Echo.Desktop/Resources/Fonts#Outfit" : name;
    private FontFamily Chain(ThemePreferences p, string first, ImportedFont? file, ImportedFont? loaded)
    {
        var a = p.Appearance ?? new();
        return new FontFamily(string.Join(", ", Slot(first, file, loaded), Slot(p.ChineseFont, a.ChineseFontFile, _loaded?.ChineseFontFile), Slot(p.FallbackFont, a.FallbackFontFile, _loaded?.FallbackFontFile), "Segoe UI", "PingFang SC", "DejaVu Sans"));
    }
    private async Task ImportAsync(string slot, string path, CancellationToken token)
    {
        if (_importing) return; _importing = true;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        try
        {
            var asset = await Task.Run(async () =>
            {
                var bytes = await ImportedFontStore.ReadAsync(path, cancel.Token); using var data = SKData.CreateCopy(bytes); using var face = SKTypeface.FromData(data);
                if (face is null || string.IsNullOrWhiteSpace(face.FamilyName)) throw new InvalidDataException("字体文件无法读取");
                var saved = await _store.SaveAsync(bytes, Path.GetExtension(path), cancel.Token); return new ImportedFont(saved, face.FamilyName);
            }, cancel.Token);
            if (!_disposed && !cancel.IsCancellationRequested) { _theme.SetFontFile(slot, asset); _theme.FontStatus = "已导入 " + asset.Family; }
        }
        finally { _importing = false; }
    }
    private async Task Work()
    {
        try
        {
            await foreach (var request in _requests.Reader.ReadAllAsync(_stop.Token))
            {
                Collection? next = null;
                try
                {
                    next = new(); long bytes = 0;
                    foreach (var file in new[] { request.MainFontFile, request.ChineseFontFile, request.FallbackFontFile, request.Typography?.SecondaryFontFile }.OfType<ImportedFont>().Distinct())
                    {
                        var data = await ImportedFontStore.ReadAsync(file.Path, _stop.Token); bytes += data.Length;
                        if (bytes > 64 * 1024 * 1024) throw new InvalidDataException("导入字体合计最大 64 MiB");
                        using var stream = new MemoryStream(data);
                        if (!next.TryAddGlyphTypeface(stream, out _)) throw new InvalidDataException("字体已损坏或格式不受支持，请重新导入");
                    }
                    var completed = next; next = null;
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (_disposed || request != _requested) { ((IDisposable)completed).Dispose(); return; }
                        var old = _collection; _collection = completed; _loaded = request;
                        FontManager.Current.AddFontCollection(completed); _refresh();
                        if (old is not null) FontManager.Current.RemoveFontCollection(old.Key);
                    });
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                { await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed) _theme.FontStatus = "导入字体不可用，已使用系统字体：" + error.Message; }); }
                finally { if (next is not null) ((IDisposable)next).Dispose(); }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    public void Dispose()
    { if (_disposed) return; _disposed = true; _theme.ImportFontAsync = null; _requests.Writer.TryComplete(); _stop.Cancel(); if (_collection is not null) FontManager.Current.RemoveFontCollection(_collection.Key); }
}
