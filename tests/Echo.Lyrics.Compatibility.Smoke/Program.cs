using System.IO.Compression;
using System.Text;
using Echo.Application.Lyrics;
using Echo.Contracts.Lyrics;
using Echo.Infrastructure.Lyrics;

using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
ParserSmoke.Run();
var root = Path.GetFullPath(Path.Combine("misc/lyrics-compatibility", Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
await using var source = new LocalLyricsSource();
await File.WriteAllTextAsync(Path.Combine(root, "Song.mp3.lrc"), "[offset:100]\n[00:01.00]Hello\n[00:03.00]world", limit.Token);
await File.WriteAllTextAsync(Path.Combine(root, "Song.mp3.t.lrc"), "[offset:300]\n[00:01.20]你好\n[00:03.20]世界", limit.Token);
await File.WriteAllTextAsync(Path.Combine(root, "Song.mp3.rom.lrc"), "[00:00.90]ni hao\n[00:02.90]shi jie", limit.Token);
var document = await Read("Song.mp3");
Check(document.Lines[0].Translation == "你好" && document.Lines[1].Romanization == "shi jie", "extended audio stem, translation/romanization and independent offsets");

Directory.CreateDirectory(Path.Combine(root, "lyrics"));
await File.WriteAllTextAsync(Path.Combine(root, "lyrics/Sub.lrc"), "[00:01.00]子目录", limit.Token);
Check((await Read("Sub.flac")).Lines[0].Text == "子目录", "lyrics subdirectory");
await File.WriteAllTextAsync(Path.Combine(root, "Tagged.zh.lrc"), "[00:01.00]中文歌词", limit.Token);
Check((await Read("Tagged.flac")).Lines[0].Text == "中文歌词", "language-tagged primary promotion");

var qrcAbsolute = LyricsParser.Parse("[10000,1000]你(10000,400)好(10400,600)", "qrc");
var qrcRelative = LyricsParser.Parse("<QrcInfos><LyricInfo LyricContent='[10000,1000]你(0,400)好(400,600)' /></QrcInfos>", "qrc");
Check(qrcAbsolute.Lines[0].Text == "你好" && qrcRelative.Lines[0].Words[1].StartMs == 10400
    && qrcAbsolute.Lines[0].Words[1].EndMs == 11000, "QRC postfix timing, absolute/relative and XML wrapper");
Check(LyricsParser.Parse("[00:01.00]<00:01.00>你<00:02.00>好", "qrc").Lines[0].Words.Count == 2, "LRC-based QRC exports");
Check(LyricsParser.Parse("[10000,1000]你(0,400)好(400,600)", "lrc").Lines[0].Words[1].StartMs == 10400,
    "QRC content detection inside LRC");
document = LyricsParser.Parse("[00:01:20][00:05:20]<00:01:20>你<00:02:20>好", "lrc");
Check(document.Lines[0].StartMs == 1200 && document.Lines[1].Words[0].StartMs == 5200, "legacy colon fractions and repeated word timeline shift");

var language = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"content\":[{\"type\":1,\"lyricContent\":[[\"你好\"]]},{\"type\":0,\"lyricContent\":[[\"ni \",\"hao\"]]}]}"));
var krc = "[language:" + language + "]\n[10000,1000]<0,400,0>你<400,600,0>好";
await File.WriteAllBytesAsync(Path.Combine(root, "Krc.krc"), EncodeKrc(krc), limit.Token);
document = await Read("Krc.mp3");
Check(document.Lines[0].Words[1].StartMs == 10400 && document.Lines[0].Translation == "你好"
    && document.Lines[0].Romanization == "ni hao", "real encrypted/compressed KRC, relative words and embedded language tracks");
Check(LyricsParser.Parse("[language:broken]\n[1000,500]<0,500,0>主歌词", "krc").Lines[0].Text == "主歌词", "broken optional KRC language metadata");

var gb = CodePagesEncodingProvider.Instance.GetEncoding(54936)!;
await File.WriteAllBytesAsync(Path.Combine(root, "Legacy.lrc"), gb.GetBytes("[00:01.00]旧编码歌词"), limit.Token);
Check((await Read("Legacy.mp3")).Lines[0].Text == "旧编码歌词", "GB18030 legacy text");
await File.WriteAllTextAsync(Path.Combine(root, "Bom.lrc"), "[00:01.00]Unicode 歌词", Encoding.Unicode, limit.Token);
Check((await Read("Bom.mp3")).Lines[0].Text == "Unicode 歌词", "UTF16 BOM");

await File.WriteAllTextAsync(Path.Combine(root, "Bad.ttml"), new string('x', 1024 * 1024 + 1), limit.Token);
await File.WriteAllTextAsync(Path.Combine(root, "Bad.lrc"), "[00:01.00]恢复", limit.Token);
Check((await Read("Bad.mp3")).Lines[0].Text == "恢复", "oversized automatic candidate falls back");
await Expect<InvalidDataException>(() => source.ReadAsync("", Path.Combine(root, "Bad.ttml"), limit.Token), "explicit oversized import fails");
await File.WriteAllBytesAsync(Path.Combine(root, "Bomb.krc"), EncodeKrc(new string('x', 1024 * 1024 + 1)), limit.Token);
await Expect<InvalidDataException>(() => source.ReadAsync("", Path.Combine(root, "Bomb.krc"), limit.Token), "decompression bound");
await File.WriteAllTextAsync(Path.Combine(root, "Song.mp3.t.lrc"), new string('x', 1024 * 1024 + 1), limit.Token);
Check((await Read("Song.mp3")).Lines[0].Text == "Hello", "broken optional companion preserves primary");
document = LyricsParser.Parse(new LyricsText("[00:01]A\n[00:01.20]B", "lrc", "test", new("[00:01]译A", "lrc")));
Check(document.Lines[0].Translation == "译A" && document.Lines[1].Translation is null, "sparse companion cannot borrow a dense neighbour");
using var canceled = new CancellationTokenSource(); canceled.Cancel();
await Expect<OperationCanceledException>(() => source.ReadAsync(Path.Combine(root, "Song.mp3"), null, canceled.Token), "read cancellation");
try { LyricsParser.Parse(string.Join('\n', Enumerable.Repeat(string.Concat(Enumerable.Repeat("[00:01]", 70)) + "a", 100)), "lrc"); throw new Exception("missing multi-stamp bound"); }
catch (InvalidDataException) { }
Console.WriteLine("PASS: existing parsers and local lyrics compatibility/failure boundaries.");

async Task<LyricsDocument> Read(string name)
{
    var text = await source.ReadAsync(Path.Combine(root, name), null, limit.Token);
    Check(text is not null, "source " + name);
    return LyricsParser.Parse(text!, limit.Token);
}
static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
static async Task Expect<T>(Func<Task<LyricsText?>> action, string name) where T : Exception
{ try { await action(); throw new Exception("missing " + name); } catch (T) { } }
static byte[] EncodeKrc(string text)
{
    using var compressed = new MemoryStream();
    using (var writer = new ZLibStream(compressed, CompressionLevel.Fastest, true)) writer.Write(Encoding.UTF8.GetBytes(text));
    var bytes = compressed.ToArray();
    byte[] key = [0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69];
    for (var i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
    return [.. "krc1"u8, .. bytes];
}
