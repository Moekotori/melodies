using System.Globalization;
using System.Text;

namespace Echo.Visuals.Canvas;

// Same alpha threshold, eight-neighbor flood fill, two fringe passes and speck merging as 02_fonts.js.
internal static class GlyphDecomposer
{
    private sealed class Box(int x,int y)
    {
        public int X0=x,Y0=y,X1=x,Y1=y,Area;
        public void Include(int x,int y){X0=Math.Min(X0,x);Y0=Math.Min(Y0,y);X1=Math.Max(X1,x);Y1=Math.Max(Y1,y);}
    }
    internal static string Split(CanvasSurface source,int res,Func<int,int,CanvasSurface> allocate,Func<CanvasSurface,int> identify)
    {
        var size=source.Width;var count=checked(size*size);var alpha=new byte[count];
        for(var p=0;p<count;p++)alpha[p]=source.Alpha(p%size,p/size);
        var labels=new int[count];var stack=new int[count];var boxes=new List<Box>{new(0,0)};
        for(var i=0;i<count;i++)
        {
            if(alpha[i]<60||labels[i]!=0)continue;
            var label=boxes.Count;var box=new Box(i%size,i/size);boxes.Add(box);
            var top=0;stack[top++]=i;labels[i]=label;
            while(top>0)
            {
                var p=stack[--top];var x=p%size;var y=p/size;box.Area++;box.Include(x,y);
                for(var dy=-1;dy<=1;dy++)for(var dx=-1;dx<=1;dx++)
                {
                    var xx=x+dx;var yy=y+dy;if(xx<0||yy<0||xx>=size||yy>=size)continue;var q=yy*size+xx;
                    if(labels[q]==0&&alpha[q]>=60){labels[q]=label;stack[top++]=q;}
                }
            }
        }
        for(var pass=0;pass<2;pass++)
        {
            var next=(int[])labels.Clone();
            for(var p=0;p<count;p++)
            {
                if(labels[p]!=0||alpha[p]==0)continue;var x=p%size;var y=p/size;var label=0;
                if(x>0&&labels[p-1]!=0)label=labels[p-1];else if(x<size-1&&labels[p+1]!=0)label=labels[p+1];
                else if(y>0&&labels[p-size]!=0)label=labels[p-size];else if(y<size-1&&labels[p+size]!=0)label=labels[p+size];
                if(label!=0){next[p]=label;boxes[label].Include(x,y);}
            }
            labels=next;
        }
        var remap=Enumerable.Range(0,boxes.Count).ToArray();var minArea=res*res*.0012;
        for(var l=1;l<boxes.Count;l++)
        {
            var box=boxes[l];if(box.Area>=minArea)continue;var best=0;var distance=1e9;var cx=(box.X0+box.X1)/2.0;var cy=(box.Y0+box.Y1)/2.0;
            for(var m=1;m<boxes.Count;m++)
            {
                if(m==l||boxes[m].Area<minArea)continue;var other=boxes[m];
                var dx=Math.Max(Math.Max(other.X0-cx,0),cx-other.X1);var dy=Math.Max(Math.Max(other.Y0-cy,0),cy-other.Y1);var d=dx*dx+dy*dy;
                if(d<distance){distance=d;best=m;}
            }
            if(best!=0){remap[l]=best;boxes[best].Include(box.X0,box.Y0);boxes[best].Include(box.X1,box.Y1);}
        }
        var pieces=new List<(int Id,int Width,int Height,double X,double Y,double Area)>();
        for(var l=1;l<boxes.Count;l++)
        {
            if(remap[l]!=l)continue;var box=boxes[l];var w=box.X1-box.X0+1;var h=box.Y1-box.Y0+1;
            var piece=allocate(w,h);
            for(var y=0;y<h;y++)for(var x=0;x<w;x++)
            {var p=(box.Y0+y)*size+box.X0+x;if(labels[p]!=0&&remap[labels[p]]==l)piece.Alpha(x,y,alpha[p]);}
            pieces.Add((identify(piece),w,h,((box.X0+box.X1+1)/2.0-size/2.0)/res,((box.Y0+box.Y1+1)/2.0-size/2.0)/res,(double)box.Area/(res*res)));
        }
        var json=new StringBuilder("[");
        foreach(var p in pieces.OrderByDescending(p=>p.Area))
        {
            if(json.Length>1)json.Append(',');
            json.Append(FormattableString.Invariant($"{{\"nativeId\":{p.Id},\"width\":{p.Width},\"height\":{p.Height},\"cx\":{p.X},\"cy\":{p.Y},\"w\":{(double)p.Width/res},\"h\":{(double)p.Height/res},\"area\":{p.Area}}}"));
        }
        return json.Append(']').ToString();
    }
}
