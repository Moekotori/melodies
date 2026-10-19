namespace Echo.Visuals.Backgrounds;

// Runtime effects do not expose fragment-quad derivatives. Keep the source fields and derive their pixel gradients explicitly.
internal static class BackgroundShaderDerivatives
{
    public static string Apply(string source)
    {
        source=source.Replace("octave < octaves; octave++) {", "octave < 4; octave++) { if (octave >= octaves) break;");
        source=source.Replace("vec2 screenDots(vec2 fragCoord", """
            float nativeDotDistance(vec2 point,float angle,float cells,float tone,float seed){
              float c=cos(angle),s=sin(angle);
              vec2 grid=mat2(c,-s,s,c)*((point-0.5*u_resolution)/u_resolution.y)*cells;
              vec2 local=fract(grid)-0.5;
              float ink=clamp(tone*(0.93+0.14*hash21(floor(grid)+seed)),0.0,1.0);
              return mix(length(local),max(abs(local.x),abs(local.y)),0.55*smoothstep(0.4,0.95,ink));
            }
            vec2 screenDots(vec2 fragCoord
            """).Replace("float edge = max(fwidth(distanceToCenter), 0.0001) * 0.85;", """
            vec2 native_pair=1.0-2.0*mod(floor(fragCoord),2.0);
            float native_dx=nativeDotDistance(fragCoord+vec2(native_pair.x,0.0),angle,cells,tone,seed)-distanceToCenter;
            float native_dy=nativeDotDistance(fragCoord+vec2(0.0,native_pair.y),angle,cells,tone,seed)-distanceToCenter;
            float edge=max(abs(native_dx)+abs(native_dy),0.0001)*0.85;
            """);
        source=source.Replace("float f = field(uv * 2.0);", """
            float f = field(uv * 2.0);
            vec2 native_df=vec2(field((uv+vec2(1.0/u_resolution.y,0.0))*2.0)-f,field((uv+vec2(0.0,1.0/u_resolution.y))*2.0)-f);
            """).Replace("dFdx(f)","native_df.x").Replace("dFdy(f)","native_df.y");
        source=source.Replace("vec2 cid2 = vec2(0.0);", "vec2 cid2 = vec2(0.0); vec2 native_r1=vec2(0.0),native_r2=vec2(0.0);")
            .Replace("d2 = d1; cid2 = cid1;", "d2 = d1; cid2 = cid1; native_r2=native_r1;")
            .Replace("d1 = d; cid1 = ip + g;", "d1 = d; cid1 = ip + g; native_r1=r;")
            .Replace("d2 = d; cid2 = ip + g;", "d2 = d; cid2 = ip + g; native_r2=r;")
            .Replace("fwidth(gap)", "((abs(native_r1.x/max(sqrt(d1),0.0001)-native_r2.x/max(sqrt(d2),0.0001))+abs(native_r1.y/max(sqrt(d1),0.0001)-native_r2.y/max(sqrt(d2),0.0001)))*4.0/u_resolution.y)");
        return source;
    }
}
