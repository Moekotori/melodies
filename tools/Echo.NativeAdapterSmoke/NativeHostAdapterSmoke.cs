using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Echo.Desktop.Features.Dsp;
using Echo.Playback;
using Echo.Playback.NativeAudio;

internal static class NativeHostAdapterSmoke
{
    public static void Run(string binary)
    {
        var session = new PlaybackSessionCoordinator(new NativeAudioHost(binary, allowProDsp: true));
        using var resources = new DspFixtureResources();
        var model = new DspViewModel(session, "Native adapter", new RelayCommand(() => { }), resources);
        try
        {
            model.RefreshCommand.Execute(null); Ready();
            var personal = Read("eq.getState");
            var profile = Read("headphoneCorrection.getState");
            profile["preampDb"] = -18.125;
            var first = profile["bands"]![0]!;
            first["frequencyHz"] = 8.5; first["gainDb"] = 20; first["q"] = 0.048;
            Write("headphoneCorrection.setState", profile);
            Select("headphone");
            Require(model.Bands.Count == 31 && model.Bands[0].FrequencyHz == 8.5
                && Math.Abs(model.Bands[0].GainDb - 20) < 0.0001 && Math.Abs(model.Bands[0].Q - 0.048) < 0.000001
                && Math.Abs(model.PreampDb + 18.125) < 0.0001, "Dedicated correction parameters preserved");
            Require(Read("eq.getState")["enabled"]!.ToJsonString() == personal["enabled"]!.ToJsonString(), "Correction cannot enable personal EQ");

            var rack = Read("dspRack.getState");
            Require(rack["order"]!.AsArray().Count == 11, "New host has eleven base rack modules");
            rack["filters"]!.AsArray().Add((JsonNode)new JsonObject
            {
                ["id"] = "filter-adapter", ["name"] = "Adapter", ["kind"] = "equalizer", ["enabled"] = false,
                ["eq"] = new JsonObject { ["preampDb"] = 0, ["bands"] = new JsonArray() }
            });
            rack["sequence"]!.AsArray().Insert(1, JsonValue.Create("filter-adapter"));
            Write("dspRack.setState", rack);
            Select("rack");
            Require(model.RackRows.Count(x => !x.Pinned) == 12, "Complete base and instance sequence");
            model.SelectedRack = model.RackRows.First(x => !x.Pinned);
            model.MoveRackDownCommand.Execute(null);
            Require(Read("dspRack.getState")["sequence"]![0]!.GetValue<string>() == "equalizer", "Rack reorder stays draft-only");
            model.MoveRackDownCommand.Execute(null); Apply();
            var reordered = Read("dspRack.getState");
            Require(reordered["filters"]!.AsArray().Count == 1
                && reordered["sequence"]![0]!.GetValue<string>() == "filter-adapter"
                && reordered["order"]![1]!.GetValue<string>() == "equalizer", "Reorder preserves filter instances and sequence slots");

            model.AddRackFilterCommand.Execute(null);
            Require(model.IsDirty, "Adding a filter marks the rack draft");
            Select("eq");
            Require(Read("dspRack.getState")["filters"]!.AsArray().Count == 1, "Leaving rack cannot commit its draft");
            var band = model.Bands[0];
            band.Enabled = true; band.Filter = EqFilters.All[0]; band.Scope = band.Scopes.Single(x => x.Key == "left");
            band.DynamicMode = band.DynamicModes.Single(x => x.Key == "down"); band.DynamicThresholdDb = -27;
            Apply();
            var dynamicState = Read("eq.getState")["bands"]![0]!;
            Require(dynamicState["scope"]!.ToString() == "left" && dynamicState["dynamicMode"]!.ToString() == "down"
                && Math.Abs(dynamicState["dynamicThresholdDb"]!.GetValue<double>() + 27) < 0.01, "Dynamic EQ and channel scope roundtrip");
            model.CompareCommand.Execute("B");
            model.Bands[0].DynamicThresholdDb = -30;
            model.CompareCommand.Execute("A");
            Require(model.Bands[0].DynamicThresholdDb == -27, "Comparison A preserves its draft");
            model.CompareCommand.Execute("B");
            Require(model.Bands[0].DynamicThresholdDb == -30, "Comparison B preserves its draft");
            Apply();
            var excessive = Read("eq.getState");
            foreach (var node in excessive["bands"]!.AsArray().Take(5))
            { node!["enabled"] = true; node["filterType"] = "peaking"; node["dynamicMode"] = "down"; }
            var rejected = false;
            try { Write("eq.setState", excessive); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "More than four active dynamic bands are rejected as a whole");
            Select("limiter");
            model.Toggles.Single(x => x.Key == "limiterAutoCeiling").Value = false;
            model.Parameters.Single(x => x.Definition.Key == "dspLimiterCeilingDb").Value = -1.5;
            model.Parameters.Single(x => x.Definition.Key == "dspLimiterReleaseMs").Value = 95;
            Apply();
            var limiter = Read("dsp.getState");
            Require(Math.Abs(limiter["dspLimiterCeilingDb"]!.GetValue<double>() + 1.5) < 0.001
                && Math.Abs(limiter["dspLimiterReleaseMs"]!.GetValue<double>() - 95) < 0.001, "Limiter ceiling and release roundtrip");
            model.Toggles.Single(x => x.Key == "limiterAutoCeiling").Value = true; Apply();
            Require(Read("dsp.getState")["dspLimiterCeilingDb"] is null, "Automatic limiter ceiling uses explicit null");
            var proceduralRack = Read("dspRack.getState");
            var procedural = DspGraphTemplates.Procedural();
            procedural["operations"]!.AsArray().Add((JsonNode)DspGraphTemplates.Operation("gain"));
            proceduralRack["procedural"] = procedural; Write("dspRack.setState", proceduralRack);
            Select("rack"); model.SelectedRack = model.RackRows.Single(x => x.Id == "procedural");
            Require(model.RackEditor is not null, "Procedural editor connects to the base rack");

            foreach (var (module, key, value) in new[]
            {
                ("multiband", "bands.1.thresholdDb", -21.5), ("deesser", "frequencyHz", 7000d), ("virtualStage", "strength", 0.5)
            })
            {
                Select(module);
                model.Parameters.Single(x => x.Definition.Key == key).Value = value;
                Apply();
                model.RefreshCommand.Execute(null); Ready();
                Require(Math.Abs(model.Parameters.Single(x => x.Definition.Key == key).Value - value) < 0.001,
                    module + " native parameter roundtrip");
            }
            foreach (var id in new[] { "replayGain", "headroom", "channel", "compressor", "crossfeed", "stereoField", "channelMatrix" })
            {
                Select(id);
                var parameter = model.Parameters[0];
                var expected = Math.Min(parameter.Maximum, parameter.Value + parameter.Step);
                parameter.Value = expected; Apply(); model.RefreshCommand.Execute(null); Ready();
                Require(Math.Abs(model.Parameters[0].Value - expected) < .001, id + " UI/native readback");
            }
            Select("deesser"); RunTask(model.SetDeesserListenAsync(true)); Require(model.DeesserListening, "Native detection listen acknowledgement");
            RunTask(model.SetDeesserListenAsync(false)); Require(!model.DeesserListening, "Detection listen stops on release");
            Select("eq"); model.PresetName = "Roundtrip"; RunTask(model.SavePresetCommand.ExecuteAsync(null));
            Require(model.Presets.Any(x => x.User && x.Label == "Roundtrip"), "Core preset visible after save");
            var exported = Path.Combine(resources.Root, "roundtrip.json"); File.WriteAllText(exported, model.ExportCurrentPreset());
            RunTask(model.ImportPresetAsync(exported)); Apply();
            var wav = Path.Combine(resources.Root, "unit.wav");
            using (var writer = new BinaryWriter(File.Create(wav)))
            {
                writer.Write("RIFF"u8); writer.Write(1060); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)2); writer.Write(48000); writer.Write(192000); writer.Write((short)4); writer.Write((short)16); writer.Write("data"u8); writer.Write(1024);
                for (var i = 0; i < 512; i++) writer.Write((short)(i < 2 ? 30000 : 0));
            }
            Select("room"); RunTask(model.ImportImpulseBankAsync(wav)); model.Enabled = true; Apply();
            Require(Read("roomCorrection.getState")["irId"]?.ToString() == model.SelectedImpulseBank?.Id, "Core bank loaded by actual native convolver");
            Select("rack"); model.RackFilterKind = model.RackFilterKinds.Single(x => x.Key == "convolution"); model.AddRackFilterCommand.Execute(null);
            model.SelectedImpulseBank = null; model.SelectedImpulseBank = model.ImpulseBanks.Single();
            Until(() => !model.ResourceBusy); Apply();
            Require(Read("dspRack.getState")["filters"]!.AsArray().Any(x => x?["kind"]?.ToString() == "convolution"), "Rack convolution resolves bank and receives native acknowledgement");
            Select("listening"); model.ListeningName = "Full chain"; RunTask(model.SaveListeningCommand.ExecuteAsync(null));
            Require(model.Profiles.Any(x => x.Name == "Full chain"), "Core listening profile saved");
            RunTask(model.ApplyListeningCommand.ExecuteAsync(null)); Require(model.Message == "方案已套用。", model.Message);
            RunTask(model.DeleteListeningCommand.ExecuteAsync(null)); Require(model.Profiles.Count == 0, "Core listening profile deleted");
            var retainedIr = session.GetRetainedDspRequestAsync("roomCorrection.loadIr").GetAwaiter().GetResult();
            var invalidIr = new JsonObject { ["path"] = Path.Combine(resources.Root, "missing.wav"), ["irId"] = "bad", ["forceReload"] = true };
            var irRejected = false;
            try { using var bad = JsonDocument.Parse("[" + invalidIr.ToJsonString() + "]"); session.DspAsync("roomCorrection.loadIr", bad.RootElement, retainForRecovery: true).GetAwaiter().GetResult(); }
            catch (IOException) { irRejected = true; }
            var retainedAfter = session.GetRetainedDspRequestAsync("roomCorrection.loadIr").GetAwaiter().GetResult();
            Require(irRejected && retainedIr?.GetRawText() == retainedAfter?.GetRawText(), "Failed IR load never replaces acknowledged recovery state");
            Console.WriteLine("PASS resource-backed presets / convolution / full listening chain / detection listen and 14 module roundtrips");
            Console.WriteLine("PASS real DSP models: correction, draft-only full rack, dynamic EQ, limiter and module roundtrips");
        }
        finally { model.Dispose(); session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }

        void RunTask(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        void Until(Func<bool> complete)
        {
            var timer = Stopwatch.StartNew();
            while (!complete()) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); if (timer.ElapsedMilliseconds > 10000) throw new TimeoutException("Resource UI operation"); }
            Dispatcher.UIThread.RunJobs();
        }
        void Select(string id) { model.SelectedModule = model.Modules.Single(x => x.Id == id); Ready(); }
        void Apply() { model.ApplyCommand.Execute(null); Ready(); Require(!model.IsDirty, model.Message); }
        void Ready()
        {
            var timer = Stopwatch.StartNew();
            do { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); if (timer.ElapsedMilliseconds > 5000) throw new TimeoutException("DSP adapter"); }
            while (model.IsBusy);
            Require(model.IsReady, model.Message);
        }
        JsonObject Read(string method)
        {
            using var args = JsonDocument.Parse("[]");
            var value = session.DspAsync(method, args.RootElement).GetAwaiter().GetResult();
            return JsonNode.Parse(value.GetRawText())!.AsObject();
        }
        void Write(string method, JsonObject state)
        {
            using var args = JsonDocument.Parse("[" + state.ToJsonString() + "]");
            session.DspAsync(method, args.RootElement).GetAwaiter().GetResult();
        }
    }

    private static void Require(bool condition, string description)
    { if (!condition) throw new IOException(description); }
}
