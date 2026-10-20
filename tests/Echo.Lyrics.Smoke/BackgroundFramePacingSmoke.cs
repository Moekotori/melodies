using Echo.Desktop.Controls.Lyrics;

internal static class BackgroundFramePacingSmoke
{
    public static void Run()
    {
        foreach(var (display,cap) in new[]{(240,240),(144,120),(144,37),(60,240),(60,10)})
        {
            var pacer=new BackgroundFramePacer();var count=0;
            for(var i=0;i<display*2;i++)if(pacer.ShouldDraw(TimeSpan.FromSeconds((double)i/display),cap))count++;
            Check(Math.Abs(count-Math.Min(display,cap)*2)<=1,$"pacing {cap} FPS on {display} Hz");
            Check(pacer.ShouldDraw(TimeSpan.FromSeconds(30),cap),"a long stall skips missed frames");
            Check(!pacer.ShouldDraw(TimeSpan.FromSeconds(30),cap),"a stall cannot create a catch-up burst");
        }
    }
    private static void Check(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}
}
