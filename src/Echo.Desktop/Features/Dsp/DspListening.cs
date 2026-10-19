using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Echo.Desktop.Features.Dsp;

public sealed record DspListeningProfile(string Id, string Name);

public sealed partial class DspViewModel
{
    private static readonly (string Read, string Write)[] ProfileModules =
    [
        ("eq.getState", "eq.setState"), ("replayGain.getConfig", "replayGain.setConfig"), ("dsp.getState", "dsp.setHeadroom"),
        ("channelBalance.getState", "channelBalance.setState"), ("compressor.getState", "compressor.setState"),
        ("crossfeed.getState", "crossfeed.setState"), ("stereoField.getState", "stereoField.setState"),
        ("channelMatrix.getState", "channelMatrix.setState"), ("dspRack.getState", "dspRack.setState"),
        ("roomCorrection.getState", "roomCorrection.setEnabled"),
        ("headphoneCorrection.getState", "headphoneCorrection.setState"),
        ("multiband.getState", "multiband.setState"), ("deesser.getState", "deesser.setState"),
        ("virtualStage.getState", "virtualStage.setState"),
    ];
    public ObservableCollection<DspListeningProfile> Profiles { get; } = [];
    [ObservableProperty] private DspListeningProfile? selectedProfile;
    [ObservableProperty] private string listeningName = "";

    private void LoadProfiles()
    {
        Profiles.Clear();
        if (DspLibraryStore.Load()["profiles"] is not JsonArray profiles) return;
        foreach (var node in profiles.Take(16))
        {
            if (node is not JsonObject profile) continue;
            var name = profile["name"]?.ToString()?.Trim();
            var id = profile["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || name.Length > 40) continue;
            Profiles.Add(new(id, name));
        }
        SelectedProfile ??= Profiles.FirstOrDefault();
    }

    [RelayCommand] private async Task SaveListeningAsync()
    {
        if (session is null) { Message = "播放服务尚未连接。"; return; }
        var name = (ListeningName.Trim() is { Length: > 0 } typed ? typed : "我的方案").Length > 40
            ? ListeningName.Trim()[..40] : (ListeningName.Trim() is { Length: > 0 } text ? text : "我的方案");
        IsBusy = true;
        try
        {
            var modules = new JsonObject();
            foreach (var (read, _) in ProfileModules)
            {
                var result = await session.DspAsync(read, Json(new JsonArray()), token: lifetime.Token);
                if (result.ValueKind == JsonValueKind.Object) modules[read] = JsonNode.Parse(result.GetRawText());
            }
            if (IrPath.Length > 0) modules["irPath"] = IrPath;
            var root = DspLibraryStore.Load();
            var profiles = root["profiles"] as JsonArray ?? new JsonArray();
            profiles.Insert(0, new JsonObject { ["id"] = Guid.NewGuid().ToString("N"), ["name"] = name, ["modules"] = modules });
            while (profiles.Count > 16) profiles.RemoveAt(profiles.Count - 1);
            root["profiles"] = profiles;
            DspLibraryStore.Save(root);
            ListeningName = ""; LoadProfiles(); Message = "方案已保存。套用时会写回原生处理链。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { Message = "方案保存失败：" + e.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private async Task ApplyListeningAsync()
    {
        if (session is null || SelectedProfile is not { } profile) return;
        if (DspLibraryStore.Load()["profiles"] is not JsonArray profiles) return;
        var stored = profiles.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.ToString() == profile.Id);
        if (stored?["modules"] is not JsonObject modules) { Message = "这个方案已经不在本机。"; return; }
        auditionEpoch++; IsBusy = true; Message = "正在套用方案…";
        try
        {
            foreach (var (read, write) in ProfileModules)
            {
                if (modules[read] is not JsonObject state) continue;
                if (read == "roomCorrection.getState")
                {
                    if (modules["irPath"]?.ToString() is { Length: > 0 } path) state["irPath"] = path;
                    await PushRoomAsync(state, retain: true, lifetime.Token);
                }
                else if (read == "dsp.getState")
                {
                    await session.DspAsync("dsp.setHeadroom", Json(new JsonArray(state["headroomDb"]!.DeepClone())), retainForRecovery: true, token: lifetime.Token);
                    await PushLimiterAsync(state, retain: true, lifetime.Token);
                }
                else if (read == "dspRack.getState")
                    await session.DspAsync(write, Json(new JsonArray(state.DeepClone())), retainForRecovery: true, token: lifetime.Token);
                else await session.DspAsync(write, Json(new JsonArray(state.DeepClone())), retainForRecovery: true, token: lifetime.Token);
            }
            await RefreshAsync();
            if (!disposed) Message = "方案已套用。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { Message = "方案套用失败：" + e.Message; IsBusy = false; }
    }

    [RelayCommand] private void DeleteListening()
    {
        if (SelectedProfile is not { } profile) return;
        var root = DspLibraryStore.Load();
        if (root["profiles"] is JsonArray profiles)
        {
            var match = profiles.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.ToString() == profile.Id);
            if (match is not null) profiles.Remove(match);
            DspLibraryStore.Save(root);
        }
        LoadProfiles();
    }
}
