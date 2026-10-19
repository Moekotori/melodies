using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Echo.Playback;

namespace Echo.Desktop.Features.Dsp;

// Only the selected graph node has controls. JSON belongs to the page draft, never to the audio data plane.
internal static class DspGraphEditor
{
    public static Control Create(JsonObject state, Action changed, string context = "equalizer")
    {
        if (state.ContainsKey("operations") || state.ContainsKey("channels")) context = "procedural";
        else if (state["kind"]?.ToString() == "headphoneCorrection") context = "headphoneCorrection";
        var panel = new StackPanel { Spacing = 10 };
        foreach (var (key, value) in state.ToArray())
        {
            if (key is "id" or "bankId" or "irName") continue;
            var kind = state["kind"]?.ToString();
            if (kind is "eq" or "gain" or "delay" or "invert" or "mute" or "mix" or "iir")
            {
                if (key == "bands" && kind != "eq" || key == "routes" && kind != "mix" || key == "coefficients" && kind != "iir"
                    || key == "gainDb" && kind != "gain" || key == "delayMs" && kind != "delay") continue;
            }
            if (value is JsonObject child)
                panel.Children.Add(new Expander { Header = Label(key), Content = Create(child, changed, context), IsExpanded = true });
            else if (value is JsonArray array)
                panel.Children.Add(key == "channels" ? Channels(array, changed) : Collection(key, array, changed, context));
            else if (value is JsonValue scalar)
                panel.Children.Add(Field(state, key, scalar, changed, context));
        }
        return panel;
    }

    private static Control Field(JsonObject owner, string key, JsonValue value, Action changed, string context)
    {
        if (value.TryGetValue<bool>(out var flag))
        {
            var check = new CheckBox { Content = Label(key), IsChecked = flag };
            check.IsCheckedChanged += (_, _) =>
            { var next = check.IsChecked == true; if (owner[key]?.ToString() == JsonValue.Create(next)!.ToString()) return; owner[key] = next; changed(); };
            return check;
        }
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,160"), ColumnSpacing = 12 };
        row.Children.Add(new TextBlock { Text = Label(key), VerticalAlignment = VerticalAlignment.Center });
        Control editor;
        if (value.GetValueKind() == System.Text.Json.JsonValueKind.Number && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            var (min, max, step) = Limits(key, context, owner["filterType"]?.ToString());
            var input = new NumericUpDown { Minimum = (decimal)min, Maximum = (decimal)max, Increment = (decimal)step, Value = (decimal)Math.Clamp(number, min, max), FormatString = "0.########" };
            input.ValueChanged += (_, _) =>
            {
                if (input.Value is not { } result) return;
                if (double.TryParse(owner[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var previous) && previous == (double)result) return;
                owner[key] = key is "input" or "output" ? JsonValue.Create((int)result) : JsonValue.Create((double)result); changed();
            };
            editor = input;
        }
        else if (Options(key, context) is { } options)
        {
            var select = new ComboBox { ItemsSource = options, SelectedItem = value.ToString(), HorizontalAlignment = HorizontalAlignment.Stretch };
            select.SelectionChanged += (_, _) => { if (select.SelectedItem is string result && owner[key]?.ToString() != result) { owner[key] = result; changed(); } };
            editor = select;
        }
        else
        {
            var input = new TextBox { Text = value.ToString(), IsReadOnly = key == "kind", MaxLength = 80 };
            input.TextChanged += (_, _) => { if (key != "kind" && owner[key]?.ToString() != (input.Text ?? "")) { owner[key] = input.Text ?? ""; changed(); } };
            editor = input;
        }
        Grid.SetColumn(editor, 1); row.Children.Add(editor); return row;
    }

    private static Control Channels(JsonArray array, Action changed)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = "作用声道（1–32，用逗号分隔）" });
        var input = new TextBox { Text = string.Join(",", array.Select(x => x?.ToString())), MaxLength = 80 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.IndianRed };
        input.TextChanged += (_, _) =>
        {
            var parts = (input.Text ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 32 || parts.Any(x => !int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n is < 1 or > 32) || parts.Distinct().Count() != parts.Length)
            { error.Text = "请输入不重复的声道编号；无效输入不会应用。"; return; }
            if (array.Select(x => x!.ToString()).SequenceEqual(parts)) { error.Text = ""; return; }
            array.Clear(); foreach (var part in parts) array.Add((JsonNode)JsonValue.Create(int.Parse(part, CultureInfo.InvariantCulture))!);
            error.Text = ""; changed();
        };
        panel.Children.Add(input); panel.Children.Add(error); return panel;
    }

    private static Control Collection(string key, JsonArray array, Action changed, string context)
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Label(key), FontWeight = FontWeight.SemiBold });
        var select = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        var body = new ContentControl();
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        var maximum = key switch { "operations" => DspGraphTemplates.MaximumOperations, "bands" => DspGraphTemplates.MaximumBands, "routes" => DspGraphTemplates.MaximumRoutes, _ => 0 };
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void Reload(int index)
        {
            select.ItemsSource = array.Select((node, i) => $"{i + 1} · {(node?["name"]?.ToString() is { Length: > 0 } name ? name : node?["kind"]?.ToString() ?? Label(key))}").ToArray();
            select.SelectedIndex = Math.Clamp(index, -1, array.Count - 1);
        }
        void Add(JsonObject node)
        {
            if (array.Count >= maximum) { message.Text = $"最多 {maximum} 项。"; return; }
            array.Add((JsonNode)node); Reload(array.Count - 1); changed();
        }
        void Button(string title, Action action)
        { var button = new Button { Content = title, Margin = new Thickness(0, 0, 6, 6) }; button.Click += (_, _) => action(); toolbar.Children.Add(button); }
        select.SelectionChanged += (_, _) => body.Content = select.SelectedIndex >= 0 && select.SelectedIndex < array.Count && array[select.SelectedIndex] is JsonObject selected ? Create(selected, changed, context) : null;
        if (key == "operations")
            foreach (var kind in new[] { "eq", "gain", "delay", "invert", "mute", "mix", "iir" })
                Button("添加" + Label(kind), () => Add(DspGraphTemplates.Operation(kind)));
        else if (key == "bands") Button("添加频段", () => Add(DspGraphTemplates.Band()));
        else if (key == "routes") Button("添加路由", () => Add(DspGraphTemplates.Route()));
        Button("复制", () =>
        {
            if (select.SelectedIndex < 0 || array[select.SelectedIndex] is not JsonObject selected) return;
            var copy = (JsonObject)selected.DeepClone();
            if (key == "operations") copy["id"] = "op-" + Guid.NewGuid().ToString("N");
            Add(copy);
        });
        Button("删除", () => { var i = select.SelectedIndex; if (i < 0) return; array.RemoveAt(i); Reload(Math.Min(i, array.Count - 1)); changed(); });
        void Move(int delta)
        {
            var i = select.SelectedIndex; var next = i + delta;
            if (i < 0 || next < 0 || next >= array.Count) return;
            var node = array[i]; array.RemoveAt(i); array.Insert(next, node); Reload(next); changed();
        }
        Button("上移", () => Move(-1)); Button("下移", () => Move(1));
        panel.Children.Add(select); panel.Children.Add(toolbar); panel.Children.Add(message); panel.Children.Add(body);
        Reload(array.Count == 0 ? -1 : 0);
        return panel;
    }

    private static string[]? Options(string key, string context) => key switch
    {
        "filterType" when context == "procedural" => ["peaking", "lowshelf", "highshelf", "lowpass", "highpass", "notch", "bandpass"],
        "filterType" => ["peaking", "lowshelf", "highshelf", "lowpass", "highpass", "notch"],
        "model" => ["bs2b", "legacy"], _ => null
    };

    private static (double Min, double Max, double Step) Limits(string key, string context, string? filterType) => key switch
    {
        "frequencyHz" when context == "procedural" => (10, 96000, 1), "q" when context == "procedural" => (0.1, 30, 0.01),
        "gainDb" when context == "procedural" => (-24, 24, 0.1),
        "b0" or "b1" or "b2" => (-16, 16, 0.0001), "a1" => (-2, 2, 0.0001), "a2" => (-0.9999, 0.9999, 0.0001),
        "frequencyHz" when context == "headphoneCorrection" => (1, 20000, 1), "q" when context == "headphoneCorrection" => (0.001, 1000, 0.001),
        "preampDb" when context == "headphoneCorrection" => (-60, 24, 0.1), "gainDb" when context == "headphoneCorrection" => (-60, 30, 0.1),
        "frequencyHz" => (20, 20000, 1), "q" => (0.1, filterType == "notch" ? 24 : 12, 0.01),
        "preampDb" => (-12, 6, 0.1), "gainDb" => (-24, 12, 0.1), "delayMs" => (0, 2000, 0.1),
        "input" or "output" => (1, 32, 1), "amount" => (0, 1, 0.01),
        "cutoffHz" => (100, 4000, 1), "feedDb" => (1, 15, 0.1), "trimDb" => (-24, 6, 0.1),
        _ => (-1000, 1000, 0.0001)
    };

    private static string Label(string key) => key switch
    {
        "enabled" => "启用", "name" => "名称", "kind" => "类型", "eq" or "equalizer" => "参数 EQ", "headphoneCorrection" => "耳机校正",
        "procedural" => "运算处理", "crossfeed" => "交叉馈送", "convolution" => "卷积", "operations" => "运算步骤", "bands" => "频段", "routes" => "混音路由",
        "preampDb" => "前级增益 (dB)", "gainDb" => "增益 (dB)", "delayMs" => "延时 (ms)", "frequencyHz" => "频率 (Hz)", "q" => "Q", "filterType" => "滤波器",
        "gain" => "增益", "delay" => "延时", "invert" => "反相", "mute" => "静音", "mix" => "混音", "iir" => "IIR",
        "input" => "输入声道", "output" => "输出声道", "coefficients" => "IIR 系数", "model" => "算法", "amount" => "混合强度",
        "cutoffHz" => "截止频率 (Hz)", "feedDb" => "交叉衰减 (dB)", "trimDb" => "卷积增益 (dB)", _ => key
    };
}
