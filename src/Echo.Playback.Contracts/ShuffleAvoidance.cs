namespace Echo.Playback;

public static class ShuffleAvoidance
{
    public static int FromSelection(string selection) => selection switch
    {
        "" or "不避重" => 0,
        "避开最近 25 首" => 25,
        "避开最近 100 首" => 100,
        _ => throw new ArgumentException("无效的随机播放避重选项。", nameof(selection))
    };
}
