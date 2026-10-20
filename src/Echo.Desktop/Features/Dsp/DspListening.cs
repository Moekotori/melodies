using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Echo.Core.Contracts;

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
    [ObservableProperty] private string activeListeningName = "当前处理链";

    public bool HasProfiles => Profiles.Count > 0;
    public bool CanManageProfile => CanSaveListening && SelectedProfile is not null;
    partial void OnSelectedProfileChanged(DspListeningProfile? value) => OnPropertyChanged(nameof(CanManageProfile));

    private async Task LoadProfilesAsync()
    {
        Profiles.Clear();
        if ((await ReadLibraryAsync())["profiles"] is not JsonArray profiles) return;
        foreach (var node in profiles.Take(16))
        {
            if (node is not JsonObject profile) continue;
            var name = profile["name"]?.ToString()?.Trim();
            var id = profile["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || name.Length > 40) continue;
            Profiles.Add(new(id, name));
        }
        SelectedProfile ??= Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(HasProfiles)); OnPropertyChanged(nameof(CanManageProfile));
    }

    [RelayCommand] private async Task SaveListeningAsync()
    {
        if (!CanSaveListening) return;
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
            if (session.RequestedOutput is { } output) modules["output.configuration"] = JsonNode.Parse(output.GetRawText());
            await resources.ExecuteAsync(DspResourceOperation.SaveProfile, id: Guid.NewGuid().ToString("N"), name: name, state: Json(new JsonObject { ["modules"] = modules }), token: lifetime.Token);
            ListeningName = ""; await LoadProfilesAsync(); Message = "方案已保存。套用时会写回原生处理链。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { Message = "方案保存失败：" + e.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand] private async Task ApplyListeningAsync()
    {
        if (session is null || !CanManageProfile || SelectedProfile is not { } profile) return;
        auditionEpoch++; IsBusy = true; Message = "正在套用方案…";
        var applying = "方案资源";
        try
        {
        if ((await ReadLibraryAsync())["profiles"] is not JsonArray profiles) throw new IOException("听音方案列表无效。");
        var stored = profiles.OfType<JsonObject>().FirstOrDefault(x => x["id"]?.ToString() == profile.Id);
        if (stored?["modules"] is not JsonObject modules) { Message = "这个方案已经不在本机。"; return; }
            foreach (var (read, write) in ProfileModules)
            {
                if (modules[read] is not JsonObject state) continue;
                applying = Modules.FirstOrDefault(x => x.ReadMethod == read)?.Title ?? read;
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
                { await ResolveRackBanksAsync(state); await session.DspAsync(write, Json(new JsonArray(state.DeepClone())), retainForRecovery: true, token: lifetime.Token); }
                else await session.DspAsync(write, Json(new JsonArray(state.DeepClone())), retainForRecovery: true, token: lifetime.Token);
            }
            if (modules["output.configuration"] is JsonObject savedOutput && session.RequestedOutput is { } currentOutput)
            {
                var output = JsonNode.Parse(currentOutput.GetRawText())!.AsObject();
                output["processing"] = savedOutput["processing"]?.DeepClone(); output["sampleRate"] = savedOutput["sampleRate"]?.DeepClone();
                await session.ConfigureOutputAsync(Json(output), lifetime.Token);
            }
            await RefreshAsync();
            ListeningAppliedRevision++;
            ActiveListeningName = profile.Name;
            if (!disposed) Message = "方案已套用。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception e) { if (!disposed) Message = $"方案未完全套用（{applying}）：{e.Message}。已确认的模块保持当前状态，请修正后重试。"; }
        finally { if (!disposed) IsBusy = false; }
    }

    [RelayCommand] private async Task DeleteListeningAsync()
    {
        if (SelectedProfile is not { } profile) return;
        IsBusy = true;
        try
        {
            await resources.ExecuteAsync(DspResourceOperation.DeleteProfile, id: profile.Id, token: lifetime.Token);
            SelectedProfile = null; await LoadProfilesAsync(); Message = "听音方案已删除。";
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!disposed) Message = "删除失败：" + error.Message; }
        finally { if (!disposed) IsBusy = false; }
    }
}
