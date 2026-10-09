using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Playback;

namespace Echo.Desktop.Features.Connect.Output;

public sealed partial class ConnectDeviceItem : ObservableObject
{
    private bool blocked;

    public ConnectDeviceItem(AudioOutputDevice device, string modeLabel, bool pinned, bool hidden,
        Action<ConnectDeviceItem> use, Action<ConnectDeviceItem> pin, Action<ConnectDeviceItem> hide)
    {
        Device = device;
        Mode = device.Mode;
        Key = KeyOf(device.Mode, device.Id);
        Name = string.IsNullOrWhiteSpace(device.Name) ? "未命名输出" : device.Name;
        ModeLabel = modeLabel;
        var rate = device.SampleRate > 0 ? device.SampleRate.ToString(CultureInfo.InvariantCulture) + " Hz" : "采样率随系统";
        Detail = device.Id.Length == 0 ? modeLabel + " · 跟随系统当前设备" : modeLabel + " · " + rate;
        CanOrganize = device.Id.IndexOfAny(['\n', '\r']) < 0 && Key.Length <= 160;
        IsPinned = pinned;
        IsHidden = hidden;
        UseCommand = new RelayCommand(() => use(this));
        PinCommand = new RelayCommand(() => pin(this));
        HideCommand = new RelayCommand(() => hide(this));
    }

    public AudioOutputDevice Device { get; }
    public string Mode { get; }
    public string Key { get; }
    public string Name { get; }
    public string ModeLabel { get; }
    public string Detail { get; }
    public bool CanOrganize { get; }
    public IRelayCommand UseCommand { get; }
    public IRelayCommand PinCommand { get; }
    public IRelayCommand HideCommand { get; }
    [ObservableProperty] private bool isPinned;
    [ObservableProperty] private bool isHidden;
    [ObservableProperty] private bool isCurrent;
    [ObservableProperty] private bool canUse = true;
    public string PinIcon => IsPinned ? "star-filled" : "star";
    public string HideLabel => IsHidden ? "恢复" : "隐藏";
    public string UseLabel => IsCurrent ? "当前" : "使用";

    public static string KeyOf(string mode, string id) => mode + "\u001f" + id;

    public void RefreshUse(bool applying)
    {
        blocked = applying;
        CanUse = !applying && !IsCurrent;
    }

    partial void OnIsPinnedChanged(bool value) => OnPropertyChanged(nameof(PinIcon));
    partial void OnIsHiddenChanged(bool value) => OnPropertyChanged(nameof(HideLabel));
    partial void OnIsCurrentChanged(bool value)
    {
        OnPropertyChanged(nameof(UseLabel));
        CanUse = !value && !blocked;
    }
}
