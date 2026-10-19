using Echo.Visuals;
using SkiaSharp;
var root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("artifacts/source-lyrics");
Directory.CreateDirectory(root);
const string project = """
{"fontSize":36,"secondarySize":18,"phase":"day","title":"此刻的回声","artist":"ECHO 歌词预览","durationMs":40000,"lines":[
{"text":"夜色沿着窗边流动","timeMs":0},
{"text":"把这一刻唱给你听","timeMs":4000,"translation":"Sing this moment to you","words":[{"text":"把","startMs":4000,"endMs":4450},{"text":"这一刻","startMs":4450,"endMs":5300},{"text":"唱给","startMs":5300,"endMs":6100},{"text":"你听","startMs":6100,"endMs":7300}]},
{"text":"让旋律带着我们远行","timeMs":8000},{"text":"微光落在你的眼睛","timeMs":12000},{"text":"每个音符都有回声","timeMs":16000},{"text":"就在此刻","timeMs":20000},{"text":"我们一起听见","timeMs":24000},{"text":"明天的声音","timeMs":28000}]}
""";
var mode=args.Length>1?args[1]:"blossom";
using var engine = new SourceLyricsEngine(mode);
Console.WriteLine(engine.Build(project,1440,900));
var coverArgument=args.FirstOrDefault(a=>a.StartsWith("--cover="));
if(coverArgument is not null)engine.SetCover(File.ReadAllBytes(coverArgument[8..]));
foreach(var ms in new[]{6000d,6001d,12000d})
{
 var watch=System.Diagnostics.Stopwatch.StartNew();
 using var picture=engine.Draw(ms,args.Contains("--motion"),ms,1000);
 Console.WriteLine($"record {watch.ElapsedMilliseconds}ms");
 using var surface=SKSurface.Create(new SKImageInfo(1440,900));surface.Canvas.DrawPicture(picture);
 using var image=surface.Snapshot();using var png=image.Encode(SKEncodedImageFormat.Png,100);
 File.WriteAllBytes(Path.Combine(root,mode+"-"+ms+".png"),png.ToArray());
 Console.WriteLine($"{ms}: {watch.ElapsedMilliseconds}ms");
 if(ms==6000 && args.Contains("--seek-check"))
 {
  var seek=engine.Pointer(mode=="cassette"?780:660,mode=="cassette"?220:450,true);
  var expected=mode=="cassette"?4000:4400;
  if(Math.Abs(seek-expected)>1)throw new InvalidOperationException($"Source seek expected {expected}ms, received {seek}");
  Console.WriteLine("PASS original scene hit test / seek timestamp");
 }
}
