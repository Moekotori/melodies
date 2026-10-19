using System.IO.Compression;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using Echo.Core.Contracts;
using Echo.Dsp;
using Echo.Playback.Processing;

internal static class DspResourceChecks
{
    public static async Task RunAsync(string evidence)
    {
        using var fixture = new DspFixtureResources();
        var eqPath = Path.Combine(fixture.Root, "rew.txt");
        await File.WriteAllTextAsync(eqPath, "Preamp: -8.125 dB\nFilter 1: ON PK Fc 75 Hz Gain -4.25 dB Q 1.25\nFilter 2: ON LS Fc 120 Hz Gain 3 dB Q 0.7");
        var imported = await fixture.ExecuteAsync(DspResourceOperation.ImportEqualizer, path: eqPath);
        Require(imported.GetProperty("bands").GetArrayLength() == 31, "REW import and bypass padding");
        await fixture.ExecuteAsync(DspResourceOperation.SavePreset, id: "check", name: "Native preset", state: imported);
        var library = await fixture.ExecuteAsync(DspResourceOperation.Library);
        Require(library.GetProperty("presets")[0].GetProperty("name").GetString() == "Native preset", "Core persists presets");
        var bankPath = Path.Combine(fixture.Root, "impulse.zip");
        using (var archive = ZipFile.Open(bankPath, ZipArchiveMode.Create))
        {
            foreach (var rate in new[] { 44100, 48000 })
            {
                using var stream = archive.CreateEntry("nested/" + rate + ".wav").Open(); using var writer = new BinaryWriter(stream);
                const int frames = 256, size = frames * 2;
                writer.Write("RIFF"u8); writer.Write(36 + size); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(size);
                for (var i = 0; i < frames; i++) writer.Write((short)(i == 0 ? 30000 : 0));
            }
        }
        var bank = await fixture.ExecuteAsync(DspResourceOperation.ImportImpulse, path: bankPath);
        Require(bank.GetProperty("filterCount").GetInt32() == 2, "Two-rate ZIP bank import");
        var resolved = await fixture.ExecuteAsync(DspResourceOperation.ResolveBank, id: bank.GetProperty("id").GetString());
        Require(File.Exists(resolved.GetProperty("nativePath").GetString()), "Core bank ID resolves to native manifest");
        var configPath = Path.Combine(fixture.Root, "routed.zip");
        using (var archive = ZipFile.Open(configPath, ZipArchiveMode.Create))
        {
            using (var writer = new BinaryWriter(archive.CreateEntry("kernel.dbl").Open())) for (var i = 0; i < 256; i++) writer.Write(i == 0 ? 1d : 0d);
            using var text = new StreamWriter(archive.CreateEntry("route.cfg").Open());
            text.Write("48000 2 2 3\n0 0\n0 0\nkernel.dbl\n0\n0.0\n1.0\nkernel.dbl\n0\n1.0\n0.0\n");
        }
        var routed = await fixture.ExecuteAsync(DspResourceOperation.ImportImpulse, path: configPath);
        var routeBank = await fixture.ExecuteAsync(DspResourceOperation.ResolveBank, id: routed.GetProperty("id").GetString());
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(routeBank.GetProperty("nativePath").GetString()!))!;
        Require(manifest["filters"]![0]!["routes"]!.AsArray().Count == 2 && manifest["filters"]![0]!["routes"]![0]!["output"]!.ToString() == "1", "Convolver CFG retains cross-channel routes and zero-as-unity coefficients");
        // Actual endpoint and production framing, isolated pipe and storage.
        using var shutdown = new CancellationTokenSource(); var pipeName = "edc-" + Guid.NewGuid().ToString("N")[..16];
        var endpoint = new DspResourceEndpoint(fixture.Service, Guid.NewGuid().ToString("N"), pipeName);
        var server = endpoint.RunAsync(shutdown.Token);
        try
        {
            var reply = await SendAsync(new(CoreDspProtocol.Version, Guid.NewGuid().ToString("N"), DspResourceOperation.Library));
            Require(reply.Error is null && reply.Result!.Value.GetProperty("presets").GetArrayLength() == 1, "Real DSP Core IPC query");
            await File.WriteAllTextAsync(eqPath, "Filter 1: ON PK Fc 75 Hz Gain 999 dB Q 1");
            reply = await SendAsync(new(CoreDspProtocol.Version, Guid.NewGuid().ToString("N"), DspResourceOperation.ImportEqualizer, Path: eqPath));
            Require(reply.Error is not null && reply.Result is null, "Invalid whole preset returns an IPC error");
        }
        finally { shutdown.Cancel(); await server; }
        foreach (var profile in new[] { "echo-hb", "echo-gauss-hires-mp", "echo-gauss-long" }.Where(id => FirProfileCatalog.Profiles.Any(x => x.Id == id)))
        {
            var plan = FirPlanBuilder.Create(48000, 768000, profile, profile, "cpu");
            Require(plan["stages"]!.AsArray().Count == 4, "16x uses bounded supported stage factors");
            foreach (var stage in plan["stages"]!.AsArray().OfType<JsonObject>())
            {
                var taps = stage["taps"]!.AsArray().Select(x => double.Parse(x!.ToString(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                Require(taps.All(double.IsFinite) && Math.Abs(taps.Sum() - 1) < .0001, "FIR DC gain and finite coefficients");
            }
        }
        var vectors = new JsonObject();
        foreach (var id in new[] { "echo-hb", "echo-gauss-hires-mp" })
        {
            var profile = FirProfileCatalog.Profiles.Single(x => x.Id == id);
            var plan = FirPlanBuilder.Create(48000, 96000, id, id, "cpu"); vectors[id] = plan["stages"]![0]!["taps"]!.DeepClone();
        }
        await File.WriteAllTextAsync(Path.Combine(evidence, "fir-vectors.json"), vectors.ToJsonString());
        // A single real OPRA fetch, retained only as verification evidence and reused if UI is recaptured.
        using var opra = new DspResourceService(Path.Combine(evidence, "opra-probe"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var matches = await opra.ExecuteAsync(new(CoreDspProtocol.Version, Guid.NewGuid().ToString("N"), DspResourceOperation.SearchOpra, Query: "sennheiser"), deadline.Token);
        Require(matches.GetProperty("items").GetArrayLength() > 0, "Live OPRA query returns headphone presets");
        var opraId = matches.GetProperty("items")[0].GetProperty("id").GetString();
        var pair = await opra.ExecuteAsync(new(CoreDspProtocol.Version, Guid.NewGuid().ToString("N"), DspResourceOperation.OpraPreset, Id: opraId), deadline.Token);
        Require(pair.GetProperty("original").GetProperty("bands").GetArrayLength() == 31 && pair.TryGetProperty("listening", out _), "Live OPRA original/listening parameter mapping");
        await File.WriteAllTextAsync(Path.Combine(evidence, "opra-result.json"), pair.GetRawText());
        Console.WriteLine("PASS Core resource persistence / real bounded IPC / strict REW / ZIP bank / multistage FIR / live OPRA");
        async Task<DspResourceResponse> SendAsync(DspResourceRequest request)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            await CoreMetadataProtocol.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(request, CoreDspJson.Default.DspResourceRequest), timeout.Token);
            return JsonSerializer.Deserialize(await CoreMetadataProtocol.ReadAsync(pipe, timeout.Token), CoreDspJson.Default.DspResourceResponse)!;
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new IOException(message); }
}
