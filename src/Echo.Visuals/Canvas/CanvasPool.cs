using Jint.Native;
using SkiaSharp;

namespace Echo.Visuals.Canvas;

// Only one scene worker accesses a pool; no shared mutable raster state.
public sealed class CanvasPool : IDisposable
{
    private const long MaxPixels = 16 * 1024 * 1024;
    private readonly Dictionary<int, CanvasSurface> surfaces = [];
    private readonly Dictionary<int, CanvasGradient> gradients = [];
    private int next; private long pixels;
    public CanvasSurface Get(int id) => surfaces[id];
    public JsValue Invoke(JsValue[] args)
    {
        var id = (int)args[0].AsNumber(); var op = args[1].AsString();
        double N(int i) => i < args.Length ? args[i].AsNumber() : 0;
        if(op=="beginFrame"){foreach(var gradient in gradients.Values)gradient.Dispose();gradients.Clear();return JsValue.Undefined;}
        if (op == "new")
        {
            if (surfaces.Count >= 1024) throw new InvalidOperationException("Afterglow canvas count exceeded.");
            var surface = new CanvasSurface(); var key = ++next; surfaces.Add(key, surface); Resize(surface, 1, 1); return key;
        }
        var c = Get(id);
        switch (op)
        {
            case "resize": Resize(c, (int)N(2), (int)N(3)); break;
            case "dispose": pixels-=(long)c.Width*c.Height;c.Dispose();surfaces.Remove(id);break;
            case "style": c.Configure(args, gradients); break;
            case "save": c.Save(); break;
            case "restore": c.Restore(); break;
            case "setTransform": c.Canvas.SetMatrix(Matrix(N(2),N(3),N(4),N(5),N(6),N(7))); break;
            case "transform": c.Canvas.Concat(Matrix(N(2),N(3),N(4),N(5),N(6),N(7))); break;
            case "translate": c.Canvas.Translate((float)N(2),(float)N(3)); break;
            case "scale": c.Canvas.Scale((float)N(2),(float)N(3)); break;
            case "rotate": c.Canvas.RotateRadians((float)N(2)); break;
            case "beginPath": c.BeginPath(); break;
            case "closePath": c.Path.Close(); break;
            case "moveTo": c.Path.MoveTo((float)N(2),(float)N(3)); break;
            case "lineTo": c.Path.LineTo((float)N(2),(float)N(3)); break;
            case "bezierCurveTo": c.Path.CubicTo((float)N(2),(float)N(3),(float)N(4),(float)N(5),(float)N(6),(float)N(7)); break;
            case "quadraticCurveTo": c.Path.QuadTo((float)N(2),(float)N(3),(float)N(4),(float)N(5)); break;
            case "rect": c.Path.AddRect(Rect(N(2),N(3),N(4),N(5))); break;
            case "roundRect": c.Path.AddRoundRect(Rect(N(2),N(3),N(4),N(5)),(float)N(6),(float)N(6)); break;
            case "arc": c.Arc(N(2),N(3),N(4),N(4),0,N(5),N(6),N(7)!=0); break;
            case "ellipse": c.Arc(N(2),N(3),N(4),N(5),N(6),N(7),N(8),N(9)!=0); break;
            case "arcTo": c.Path.ArcTo((float)N(2),(float)N(3),(float)N(4),(float)N(5),(float)N(6)); break;
            case "fill": c.Canvas.DrawPath(c.Path,c.Fill); break;
            case "stroke": c.Canvas.DrawPath(c.Path,c.Stroke); break;
            case "clip": c.Canvas.ClipPath(c.Path,SKClipOperation.Intersect,true); break;
            case "fillRect": c.Canvas.DrawRect(Rect(N(2),N(3),N(4),N(5)),c.Fill); break;
            case "strokeRect": c.Canvas.DrawRect(Rect(N(2),N(3),N(4),N(5)),c.Stroke); break;
            case "clearRect": using (var paint = new SKPaint{BlendMode=SKBlendMode.Clear}) c.Canvas.DrawRect(Rect(N(2),N(3),N(4),N(5)),paint); break;
            case "fillText": c.Text(args[2].AsString(),(float)N(3),(float)N(4),false); break;
            case "strokeText": c.Text(args[2].AsString(),(float)N(3),(float)N(4),true); break;
            case "measure": return c.Measure(args[2].AsString());
            case "drawImage": c.Image(Get((int)N(2)), args); break;
            case "pixels": c.WritePixels(args[2].AsObject(),(int)N(3),(int)N(4),(int)N(5),(int)N(6));break;
            case "grain": c.Grain(); break;
            case "decompose": return GlyphDecomposer.Split(c,(int)N(2),Allocate,surface=>surfaces.First(pair=>ReferenceEquals(pair.Value,surface)).Key);
            case "paperNoise": c.PaperNoise(); break;
            case "readPixels": return c.ReadPixels((int)N(2),(int)N(3),(int)N(4),(int)N(5));
            case "getTransform": return c.Transform();
            case "pattern":
                var patternId=++next;gradients.Add(patternId,new CanvasGradient(Get((int)N(2)).Pattern(args[3].AsString())));return patternId;
            case "gradient":
                if (gradients.Count > 4096) throw new InvalidOperationException("Afterglow gradient count exceeded.");
                var gradientId = ++next; gradients.Add(gradientId,new CanvasGradient(args)); return gradientId;
            case "stop": gradients[(int)N(2)].Add((float)N(3),args[4].AsString()); break;
            case "dash": c.Dash(args[2].AsString()); break;
            default: throw new NotSupportedException("Canvas operation: " + op);
        }
        return JsValue.Undefined;
    }
    private void Resize(CanvasSurface c, int w, int h)
    {
        w = Math.Clamp(w,1,4096); h = Math.Clamp(h,1,4096);
        var cost = (long)w*h; var before = (long)c.Width*c.Height;
        if (pixels - before + cost > MaxPixels) throw new InvalidOperationException("Afterglow raster budget exceeded.");
        c.Resize(w,h); pixels = pixels - before + cost;
    }
    private CanvasSurface Allocate(int width,int height)
    {
        if(surfaces.Count>=1024)throw new InvalidOperationException("Afterglow canvas count exceeded.");
        var surface=new CanvasSurface();Resize(surface,width,height);surfaces.Add(++next,surface);return surface;
    }
    internal static SKRect Rect(double x,double y,double w,double h) => SKRect.Create((float)x,(float)y,(float)w,(float)h);
    private static SKMatrix Matrix(double a,double b,double c,double d,double e,double f)
        => new((float)a,(float)c,(float)e,(float)b,(float)d,(float)f,0,0,1);
    public void Dispose() { foreach(var c in surfaces.Values)c.Dispose(); foreach(var g in gradients.Values)g.Dispose();surfaces.Clear();gradients.Clear();pixels=0; }
}
