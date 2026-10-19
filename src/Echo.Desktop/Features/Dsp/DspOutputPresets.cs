using Echo.Playback.Processing;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    private void UpdateOutputPreset(DspChoiceField changed)
    {
        if (!IsSdm || projecting) return;
        projecting = true;
        try
        {
            if (changed.Key == "soundProfile" && FirProfileCatalog.Sounds.FirstOrDefault(x => x.Id == changed.Selected.Key) is { } sound)
            {
                Select("filter1x", sound.First); Select("filterNx", sound.Next);
                Select("preModulatorProfile", sound.Id == "ultimate-transient" ? "ultimate-transient" : sound.Id.StartsWith("ultimate-", StringComparison.Ordinal) ? "ultimate-reference" : "standard");
            }
            else if (changed.Key is "filter1x" or "filterNx")
            {
                var first = Choices.FirstOrDefault(x => x.Key == "filter1x")?.Selected.Key;
                var next = Choices.FirstOrDefault(x => x.Key == "filterNx")?.Selected.Key;
                Select("soundProfile", FirProfileCatalog.Sounds.FirstOrDefault(x => x.First == first && x.Next == next)?.Id ?? "custom");
            }
        }
        finally { projecting = false; }
        void Select(string key, string value)
        { if (Choices.FirstOrDefault(x => x.Key == key) is { } field && field.Options.FirstOrDefault(x => x.Key == value) is { } choice) field.Selected = choice; }
    }
}
