using System.Diagnostics;
using System.Text.Json;
using Echo.Visuals;
var watch=Stopwatch.StartNew();
using var engine=new JizuraEngine();Console.WriteLine($"Scripts: {engine.ScriptCount}; startup {watch.ElapsedMilliseconds} ms");
var root=Path.GetFullPath("misc/lyrics-completeness/frames");Directory.CreateDirectory(root);
var project="""{"title":"此刻的回声","artist":"ECHO","duration":12,"seed":20261002,"style":"noir","look":"mist","lines":[{"text":"把这一刻唱给你听","start":0,"end":4,"timings":[{"text":"把","from":0,"to":1,"start":0,"end":4}]},{"text":"让旋律带着我们远行","start":4,"end":8},{"text":"微光落在你的眼睛","start":8,"end":12}]}""";
var looks=JsonDocument.Parse(File.ReadAllText("src/Echo.Visuals/Resources/Jizura/looks.json"));
foreach(var look in looks.RootElement.EnumerateArray())
{
    var id=look.GetProperty("id").GetString()!;var start=watch.ElapsedMilliseconds;
    try
    {
        Console.WriteLine(engine.Build(project.Replace("\"look\":\"mist\"","\"look\":\""+id+"\"",StringComparison.Ordinal)));
        var frame=engine.Draw(2,true);File.WriteAllBytes(Path.Combine(root,id+".png"),frame);
        Console.WriteLine($"PASS {id} {frame.Length} bytes {watch.ElapsedMilliseconds-start} ms");
    }
    catch(Exception ex){Console.WriteLine($"FAIL {id}: {ex.Message}");Environment.ExitCode=1;}
}
Console.WriteLine($"All original looks checked ({watch.ElapsedMilliseconds} ms)");
