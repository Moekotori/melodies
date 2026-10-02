using Avalonia.Controls;
using Avalonia.Threading;

namespace Echo.Desktop.Localization;

// A window-local resource provider; DynamicResource subscriptions follow Avalonia view lifetimes.
internal sealed class WindowLocalizationBinding : IDisposable
{
    private readonly Control _window;
    private readonly LocalizationService _text;
    private ResourceDictionary _resources = new();
    public WindowLocalizationBinding(Control window, LocalizationService text)
    {
        _window = window; _text = text; Apply(); text.Changed += Apply;
    }
    private void Apply()
    {
        Dispatcher.UIThread.VerifyAccess();
        var next = new ResourceDictionary();
        foreach (var key in _text.Catalog.Fallback.Strings.Keys) next.Add(key, _text[key]);
        // Replace once, rather than publishing a resource-change event per string.
        var dictionaries = _window.Resources.MergedDictionaries;
        var index = dictionaries.IndexOf(_resources);
        if (index < 0) dictionaries.Add(next); else dictionaries[index] = next;
        _resources = next;
    }
    public void Dispose() { _text.Changed -= Apply; _window.Resources.MergedDictionaries.Remove(_resources); }
}
