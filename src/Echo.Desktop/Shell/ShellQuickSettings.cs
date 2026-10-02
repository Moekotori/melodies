using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Shell;

public sealed partial class ShellViewModel
{
    [ObservableProperty] private string quickSettingsKind = "";
    public bool IsQuickSettingsOpen => QuickSettingsKind.Length > 0;
    public string QuickSettingsTitle => QuickSettingsKind == "audio" ? "音频设置" : "歌词设置";
    [RelayCommand] private void OpenQuickSettings(string? kind)
    {
        if (kind is not ("audio" or "lyrics")) return;
        CloseSidebarDrawerCommand.Execute(null); CloseQueueDrawerCommand.Execute(null);
        QuickSettingsKind = QuickSettingsKind == kind ? "" : kind;
    }
    [RelayCommand] private void CloseQuickSettings() => QuickSettingsKind = "";
    partial void OnQuickSettingsKindChanged(string value)
    { OnPropertyChanged(nameof(IsQuickSettingsOpen)); OnPropertyChanged(nameof(QuickSettingsTitle)); }
}
