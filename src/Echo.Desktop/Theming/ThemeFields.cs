using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Theming;

public sealed partial class ThemeColorField : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string Hint => Key switch
    {
        "appBg" => "页面的底色", "accent" => "按钮、进度与选中标记", "accentStrong" => "强调文字与重要链接",
        "secondary" => "辅助元素", "heading" => "标题与重要内容", "muted" => "说明与次要信息",
        "panel" => "内容面板与浮层", "appBg2" => "背景渐变的中间色", "appBg3" => "背景渐变的末端色",
        "panelSoft" => "轻量面板与弱分区", "text" => "正文与列表文字", "border" => "轮廓与分隔线",
        "onAccent" => "强调按钮上的文字", "buttonText" => "普通按钮上的文字", "titlebar" => "窗口顶部",
        "sidebar" => "主导航区域", "player" => "底部播放区域", "field" => "输入与选择控件",
        "row" => "列表的底色", "rowHover" => "鼠标经过的列表行", "rowActive" => "当前选中的列表行",
        "chip" => "标签与小型标记", "success" => "成功与完成状态", "warning" => "需要留意的提醒",
        "danger" => "错误与失败状态", _ => "键盘操作时的焦点"
    };
    private readonly Action<string, string> _commit;
    private readonly string _default;
    private bool _sync;
    private bool _fromHsv;
    [ObservableProperty] private string value;
    [ObservableProperty] private string error = "";
    [ObservableProperty] private double red;
    [ObservableProperty] private double green;
    [ObservableProperty] private double blue;
    [ObservableProperty] private double alpha = 255;
    [ObservableProperty] private double hue;
    [ObservableProperty] private double saturation;
    [ObservableProperty] private double brightness;
    public IBrush Swatch => Color.TryParse(Value, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;
    public double OpacityPercent => Alpha / 255 * 100;
    public ThemeColorField(ThemeColorDefinition definition, string initial, string fallback, Action<string, string> commit)
    { Key = definition.Key; Label = definition.Label; value = initial; _default = fallback; _commit = commit; SyncRgb(); }
    partial void OnValueChanged(string value)
    {
        if (!ThemeColorMath.TryHex(value, out var hex)) { Error = "请输入 #RGB、#RRGGBB 或 #AARRGGBB"; return; }
        Error = ""; OnPropertyChanged(nameof(Swatch)); SyncRgb(); _commit(Key, hex);
    }
    private void SyncRgb()
    {
        if (!Color.TryParse(Value, out var c)) return;
        _sync = true; Red = c.R; Green = c.G; Blue = c.B; Alpha = c.A; _sync = false;
        if (!_fromHsv)
        {
            _sync = true; (Hue, Saturation, Brightness) = ThemeColorMath.ToHsv(c); _sync = false;
        }
    }
    private void CommitRgb() { if (!_sync) Value = Alpha == 255 ? $"#{(int)Red:x2}{(int)Green:x2}{(int)Blue:x2}" : $"#{(int)Alpha:x2}{(int)Red:x2}{(int)Green:x2}{(int)Blue:x2}"; }
    partial void OnRedChanged(double value) => CommitRgb();
    partial void OnGreenChanged(double value) => CommitRgb();
    partial void OnBlueChanged(double value) => CommitRgb();
    partial void OnAlphaChanged(double value) { CommitRgb(); OnPropertyChanged(nameof(OpacityPercent)); }
    private void CommitHsv()
    {
        if (_sync) return;
        var c = ThemeColorMath.FromHsv(Hue, Saturation, Brightness);
        _fromHsv = true;
        try { Value = Alpha == 255 ? $"#{c.R:x2}{c.G:x2}{c.B:x2}" : $"#{(int)Alpha:x2}{c.R:x2}{c.G:x2}{c.B:x2}"; }
        finally { _fromHsv = false; }
    }
    partial void OnHueChanged(double value) => CommitHsv();
    partial void OnSaturationChanged(double value) => CommitHsv();
    partial void OnBrightnessChanged(double value) => CommitHsv();
    [RelayCommand] private void Reset() => Value = _default;
}
public sealed partial class ThemeNumberField : ObservableObject
{
    public ThemeNumberDefinition Definition { get; }
    private readonly Action<string, double> _commit;
    [ObservableProperty] private double value;
    public ThemeNumberField(ThemeNumberDefinition definition, double initial, Action<string, double> commit)
    { Definition = definition; value = initial; _commit = commit; }
    partial void OnValueChanged(double value) { if (double.IsFinite(value)) _commit(Definition.Key, Math.Clamp(value, Definition.Min, Definition.Max)); }
}
public sealed record ThemeColorGroup(string Title, IReadOnlyList<ThemeColorField> Fields)
{
    public string ShortTitle => Title switch { "基础配色" => "基础", "渐变与文字" => "文字与渐变", "界面区域" => "界面", _ => "状态" };
}
