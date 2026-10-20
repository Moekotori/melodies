using System.Text.RegularExpressions;

namespace Echo.Visuals.Backgrounds;

internal static partial class BackgroundShaderCompatibility
{
    public static string Apply(string source)
    {
        var functions=new List<string>();
        source=Array().Replace(source,m=>
        {
            var type=m.Groups[1].Value;var name=m.Groups[2].Value;var values=m.Groups[3].Value.Split(',');
            functions.Add(type+" native_"+name+"(int i){"+string.Join("",values.Select((v,i)=>$"if(i=={i})return {v.Trim()};"))+"return "+values[^1].Trim()+";}");
            return "";
        });
        foreach(var function in functions)
        {
            var name=Regex.Match(function,@"native_(\w+)\(").Groups[1].Value;
            source=Regex.Replace(source,@"\b"+name+@"\[([^\]]+)\]", "native_"+name+"($1)");
        }
        if(source.Contains("u_hist[23 - lagRow]",StringComparison.Ordinal))
        {
            // SkSL requires constant uniform-array indices. Newest rows are most common,
            // so test them first; this preserves the original history interpolation.
            functions.Add("vec3 native_skylineHistory(int i){"+string.Join("",Enumerable.Range(1,23).Reverse().Select(i=>$"if(i=={i})return u_hist[{i}];"))+"return u_hist[0];}");
            source=source.Replace("u_hist[23 - lagRow]","native_skylineHistory(23-lagRow)")
                .Replace("u_hist[22 - lagRow]","native_skylineHistory(22-lagRow)");
        }
        source=source.Replace("for (int row = firstRow; row < 24; row++) {", "for (int row = 0; row < 24; row++) { if(row < firstRow)continue;")
            .Replace("clamp(int(ceil(fraction * 23.0)), 0, 24)","int(clamp(ceil(fraction * 23.0),0.0,24.0))")
            .Replace("for (int j = 0; j < span; j++) {","for (int j = 0; j < 2; j++) { if(j >= span)continue;")
            .Replace("for (int i = 0; i < span; i++) {","for (int i = 0; i < 2; i++) { if(i >= span)continue;");
        source=source.Replace("float((native_FONT(n) >> (14 - (row * 3 + col))) & 1)","mod(floor(float(native_FONT(n))/exp2(float(14-(row*3+col)))),2.0)");
        source=Remainder().Replace(source,"($1-($1/3)*3)");
        if(source.Contains("vec2 gP[9];",StringComparison.Ordinal))
        {
            var globals=string.Join("",Enumerable.Range(0,9).Select(i=>$"vec2 native_gp{i};"));
            var getter="vec2 native_getGP(int i){"+string.Join("",Enumerable.Range(0,8).Select(i=>$"if(i=={i})return native_gp{i};"))+"return native_gp8;}";
            var setter="void native_setGP(int i,vec2 value){"+string.Join("",Enumerable.Range(0,9).Select(i=>$"if(i=={i})native_gp{i}=value;"))+"}";
            source=source.Replace("vec2 gP[9];",globals+getter+setter);
            source=Regex.Replace(source,@"gP\[idx\]\s*=\s*([^;]+);","native_setGP(idx,$1);");
            source=Regex.Replace(source,@"gP\[([^\]]+)\]","native_getGP($1)");
        }
        if(source.Contains("float gDist[18];",StringComparison.Ordinal))
        {
            var globals=string.Join("",Enumerable.Range(0,18).Select(i=>$"float native_gd{i};"));
            var getter="float native_getGD(int i){"+string.Join("",Enumerable.Range(0,17).Select(i=>$"if(i=={i})return native_gd{i};"))+"return native_gd17;}";
            var setter="void native_setGD(int i,float value){"+string.Join("",Enumerable.Range(0,18).Select(i=>$"if(i=={i})native_gd{i}=value;"))+"}";
            source=source.Replace("float gDist[18];",globals+getter+setter);
            source=Regex.Replace(source,@"gDist\[idx \* 2 \+ k\]\s*=\s*([^;]+);","native_setGD(idx*2+k,$1);");
            source=Regex.Replace(source,@"gDist\[([^\]]+)\]","native_getGD($1)");
        }
        source=source.Replace("float diagFlip[9];","");
        source=Regex.Replace(source,@"diagFlip\[idx\] = [^;]+;", "");
        source=Regex.Replace(source,@"diagFlip\[(0|1|3|4)\]",m=>{
            var index=int.Parse(m.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture);
            return $"step(0.5,hash21(base+vec2({index%3-1}.0,{index/3-1}.0)+13.0))";
        });
        return string.Join("\n",functions)+"\n"+source;
    }
    [GeneratedRegex(@"const\s+(float|int)\s+(\w+)\[\d+\]\s*=\s*(?:float|int)\[\d+\]\(([^;]+)\);",RegexOptions.Singleline)] private static partial Regex Array();
    [GeneratedRegex(@"\b(ia|idx)\s*%\s*3")] private static partial Regex Remainder();
}
