using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Echo.Contracts.Settings;
using Echo.Desktop.Features.Floating;

namespace Echo.Desktop.Controls.Lyrics;

public sealed partial class FloatingLyricsControl
{
    private void ReadTypeface()
    {
        try
        {
            var family = customFont ? medium.FontFamily : new FontFamily(settings?.FontFamily is { Length: > 0 and <= 256 } s ? s : DesktopLyricsFont.Default);
            medium = new(family, FontStyle.Normal, FontWeight.Medium);
            regular = new(family, FontStyle.Normal, FontWeight.Normal);
        }
        catch (Exception error) when (error is ArgumentException or UriFormatException)
        {
            medium = new(new FontFamily(DesktopLyricsFont.Default), FontStyle.Normal, FontWeight.Medium);
            regular = new(medium.FontFamily, FontStyle.Normal, FontWeight.Normal);
        }
    }
    private void QueueFont()
    {
        var path = settings?.FontFilePath ?? "";
        var token = ++fontToken;
        if (path.Length == 0) { ClearFont(); ReadTypeface(); cache.Dispose(); return; }
        Task.Run(() =>
        {
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length is 0 or > 32 * 1024 * 1024) return null;
                return File.ReadAllBytes(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
        }).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            if (disposed || token != fontToken) return;
            InstallFont(task.Status == TaskStatus.RanToCompletion ? task.Result : null);
        }));
    }
    private void InstallFont(byte[]? bytes)
    {
        ClearFont();
        if (bytes is null) { ReadTypeface(); cache.Dispose(); Refresh(); return; }
        var bag = new FontBag();
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            if (!bag.TryAddGlyphTypeface(stream, out var face) || string.IsNullOrWhiteSpace(face.FamilyName)) ((IDisposable)bag).Dispose();
            else
            {
                FontManager.Current.AddFontCollection(bag);
                fonts = bag; customFont = true;
                var family = new FontFamily(bag.Key.AbsoluteUri + "#" + face.FamilyName);
                medium = new(family, FontStyle.Normal, FontWeight.Medium);
                regular = new(family, FontStyle.Normal, FontWeight.Normal);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or IOException or UriFormatException)
        { ((IDisposable)bag).Dispose(); customFont = false; }
        cache.Dispose(); Refresh();
    }
    private void ClearFont()
    {
        customFont = false;
        if (fonts is null) return;
        var bag = fonts; fonts = null;
        try { FontManager.Current.RemoveFontCollection(bag.Key); } catch (InvalidOperationException) { }
        ((IDisposable)bag).Dispose();
    }
    private sealed class FontBag : FontCollectionBase { public override Uri Key { get; } = new("fonts:echo-desktop-lyrics"); }
}
