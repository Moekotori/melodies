using Echo.Desktop.Motion;

namespace Echo.Desktop.Shell;

// Pages enter from the side of their place in the (user-ordered) sidebar; settings sits after all.
public sealed partial class ShellViewModel
{
    public MotionDirection RouteDirection(string from, string to)
    {
        if (from == to) return MotionDirection.None;
        var origin = RouteOrder(from);
        var target = RouteOrder(to);
        return origin < 0 || target < 0 ? MotionDirection.None
            : target > origin ? MotionDirection.Forward : MotionDirection.Back;
    }
    private int RouteOrder(string id)
    {
        if (id == "settings") return int.MaxValue;
        var index = 0;
        foreach (var item in AllItems())
        {
            if (item.Id == id) return index;
            index++;
        }
        return -1;
    }
}
