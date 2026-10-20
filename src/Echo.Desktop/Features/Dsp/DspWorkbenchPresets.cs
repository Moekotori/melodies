using System.Globalization;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    [RelayCommand]
    private void WorkbenchPreset(string value)
    {
        if (!CanEdit) return;
        var parts = value.Split(':', 2);
        if (parts.Length != 2) return;
        projecting = true;
        try
        {
            switch (parts[0])
            {
                case "headroom" when SelectedModule.Id == "headroom": Set("headroomDb", double.Parse(parts[1], CultureInfo.InvariantCulture)); break;
                case "trim" when IsRoom: Set("trimDb", double.Parse(parts[1], CultureInfo.InvariantCulture)); break;
                case "width" when SelectedModule.Id == "stereoField": Set("width", double.Parse(parts[1], CultureInfo.InvariantCulture)); break;
                case "crossfeed" when SelectedModule.Id == "crossfeed":
                    var feed = parts[1] switch { "subtle" => (.12, 650), "natural" => (.25, 700), "strong" => (.4, 900), "speaker" => (.6, 1200), _ => throw new ArgumentException("Unknown crossfeed preset") };
                    Set("amount", feed.Item1); Set("cutoffHz", feed.Item2); break;
                case "stage" when SelectedModule.Id == "virtualStage":
                    var stage = parts[1] switch { "light" => (.25, 20, .4), "natural" => (.45, 30, 1d), "strong" => (.7, 45, 1d), _ => throw new ArgumentException("Unknown virtual stage preset") };
                    Set("strength", stage.Item1); Set("azimuthDegrees", stage.Item2); Set("reflection", stage.Item3); break;
                case "bs2b" when SelectedModule.Id == "crossfeed":
                    var bauer = parts[1] switch { "bauer" => (700, 4.5), "chu-moy" => (700, 6d), "jan-meier" => (650, 9.5), _ => throw new ArgumentException("Unknown BS2B preset") };
                    Set("cutoffHz", bauer.Item1); Set("feedDb", bauer.Item2); break;
                default: return;
            }
        }
        finally { projecting = false; }
        IsDirty = true; NotifyWorkbench(); NoteEdit();
        void Set(string key, double number) => Fields[key].Value = number;
    }
}
