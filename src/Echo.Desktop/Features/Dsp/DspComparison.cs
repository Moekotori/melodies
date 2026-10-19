using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed partial class DspViewModel
{
    // Two page-local snapshots, released on module change. Rack comparisons remain drafts.
    private JsonObject? comparisonA, comparisonB;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsComparisonA)), NotifyPropertyChangedFor(nameof(IsComparisonB))]
    private string comparisonSlot = "A";
    public bool IsComparisonA => ComparisonSlot == "A";
    public bool IsComparisonB => ComparisonSlot == "B";

    [RelayCommand] private void Compare(string slot)
    {
        if (!CanEdit || confirmed is null || SelectedModule.WriteMethod is null || slot is not ("A" or "B") || slot == ComparisonSlot) return;
        try
        {
            var current = CaptureDraft();
            if (ComparisonSlot == "A") comparisonA = current; else comparisonB = current;
            var target = (slot == "A" ? comparisonA : comparisonB) ?? current;
            Project(target, updateConfirmed: false); ComparisonSlot = slot;
            IsDirty = true; NoteEdit();
        }
        catch (Exception e) { Message = "无法切换对比：" + e.Message; }
    }

    private void ClearComparison() { comparisonA = null; comparisonB = null; ComparisonSlot = "A"; }
}
