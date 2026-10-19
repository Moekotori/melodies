using System.Diagnostics;
using System.Text.RegularExpressions;
using Echo.Contracts.Library;
using Echo.Contracts.Media;
using Echo.Contracts.Scanning;
using Echo.Infrastructure.Files;
using Echo.Infrastructure.Metadata;
using Echo.Infrastructure.Playback;
using Echo.Playback;
using Echo.Playback.NativeAudio;

if (args.Length < 4) throw new ArgumentException("args: ffmpeg scanner audio-host legacy-root");
using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(50));
var token = budget.Token;
var timer = Stopwatch.StartNew();
await FormatCapabilities.CheckAsync(args[1], token);
if (args.Contains("--capabilities-only")) { Console.WriteLine("PASS native/managed built-in format parity and deployed FFmpeg capability"); return; }
var root = Path.GetFullPath(Path.Combine("misc", "formats", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
if (args.Contains("--wave-only"))
{
    await WaveVariants.CheckAsync(args[0], root, args[1], token); return;
}
var legacyText = await File.ReadAllTextAsync(Path.Combine(args[3], "src", "shared", "constants", "audioExtensions.ts"), token);
var declaration = legacyText[..legacyText.IndexOf("] as const", StringComparison.Ordinal)];
var legacy = Regex.Matches(declaration, "'(?<extension>\\.[a-z0-9]+)'").Select(x => x.Groups["extension"].Value).ToArray();
Check(legacy.SequenceEqual(AudioFormatCatalog.LegacyExtensions.Where(x => AudioFormatCatalog.Route(x) != AudioFormatRoute.PluginContainer)),
    "legacy built-in format catalogue drift");
Check(legacy.All(x => ScanFormats.Supports(x.ToUpperInvariant())), "scan candidate format gap");
var fixtures = await FormatFixtures.CreateAsync(args[0], root, token, args.Contains("--adpcm-only"));
if (!args.Contains("--adpcm-only"))
    await FlacVariants.CheckAsync(fixtures[1].Path, root, args[1], token);
var selectedFixtures = args.Contains("--remaining") ? fixtures.Skip(20) : fixtures;
await using var metadata = new LocalMetadataReader(args[1]);
await using var session = args.Contains("--metadata-only") ? null : new PlaybackSessionCoordinator(new NativeAudioHost(args[2]));
if (session is not null) await session.SetVolumeAsync(0, token);
var passed = 0;
foreach (var fixture in selectedFixtures)
{
    var file = new FileInfo(fixture.Path);
    var data = await metadata.ReadAsync(new(file.FullName, LibraryPath.Key(file.FullName), file.Length, file.LastWriteTimeUtc.Ticks), token);
    Check(data.SampleRate > 0 && data.DurationSeconds >= 0 && data.Codec.Length > 0, fixture.Name + " actual metadata");
    if (fixture.Name == "WAV/ADPCM") Check(data.Codec.Contains("ADPCM", StringComparison.Ordinal), "compressed WAV was mislabeled as PCM/container");
    if (session is not null)
    {
        await session.PlayAsync(new(fixture.Name, fixture.Name, fixture.Path), token);
        await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing || session.Snapshot.Status == PlaybackStatus.Faulted, token);
        Check(session.Snapshot.Status == PlaybackStatus.Playing, fixture.Name + " first-frame native output: " + session.Snapshot.Error);
        await session.StopAsync(token);
    }
    Console.WriteLine($"PASS {fixture.Name}: metadata={data.Codec}/{data.SampleRate}" + (session is null ? "" : ", decoded native frames"));
    passed++;
}
var wav = fixtures[0].Path;
var cuePath = Path.Combine(root, "album.cue");
await File.WriteAllTextAsync(cuePath, $"TITLE \"Cue album\"\nPERFORMER \"Cue artist\"\nFILE \"{Path.GetFileName(wav)}\" WAVE\n TRACK 01 AUDIO\n  TITLE \"First\"\n  INDEX 01 00:00:00\n TRACK 02 AUDIO\n  TITLE \"Second\"\n  INDEX 01 00:00:12\n", token);
var cue = await PlaybackFileResolver.ResolveAsync([cuePath], token);
Check(cue.Count == 2 && cue[0].Source?.Range?.EndSeconds == 0.16 && cue[1].Source?.Range?.StartSeconds == 0.16, "direct CUE range expansion");
if (session is not null)
{
    await session.PlayAsync(cue[1], token);
    await UntilAsync(() => session.Snapshot.Status == PlaybackStatus.Playing, token);
    await session.StopAsync(token);
}
var osuPath = Path.Combine(root, "beatmap.osu");
await File.WriteAllTextAsync(osuPath, $"osu file format v14\n[General]\nAudioFilename: {Path.GetFileName(wav)}\n[Metadata]\nTitle: ECHO osu fixture\nArtist: Artist\nCreator: Mapper\n", token);
var osu = await PlaybackFileResolver.ResolveAsync([osuPath], token);
Check(osu.Count == 1 && osu[0].Source?.Uri == wav && osu[0].Metadata?.GetValueOrDefault("title") == "ECHO osu fixture", "direct osu source resolution");
var ncm = Path.Combine(root, "encrypted.ncm"); await File.WriteAllTextAsync(ncm, "not an audio decoder input", token);
try { await PlaybackFileResolver.ResolveAsync([ncm], token); throw new Exception("encrypted container advertised as builtin decode"); }
catch (AudioFormatException e) when (e.Code == "audio_format_provider_required") { }
try
{
    var video = new FileInfo(Path.Combine(root, "video-only.webm"));
    await metadata.ReadAsync(new(video.FullName, LibraryPath.Key(video.FullName), video.Length, video.LastWriteTimeUtc.Ticks), token);
    throw new Exception("video-only file imported as audio");
}
catch (MetadataReadException e) when (e.Code == "native_format_unsupported") { }
Console.WriteLine($"PASS {passed} " + (session is null ? "metadata" : "native decode/metadata")
    + $" variants, {legacy.Length} legacy entries, CUE/osu source resolution, provider boundary, no-audio rejection ({timer.ElapsedMilliseconds} ms)");
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task UntilAsync(Func<bool> condition, CancellationToken token)
{
    var clock = Stopwatch.StartNew();
    while (!condition()) { token.ThrowIfCancellationRequested(); if (clock.ElapsedMilliseconds > 1500) throw new TimeoutException("native first-frame confirmation"); await Task.Delay(5, token); }
}
