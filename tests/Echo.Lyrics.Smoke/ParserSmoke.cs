using Echo.Application.Lyrics;

static class ParserSmoke
{
    public static void Run()
    {
        var lrc = LyricsParser.Parse("[offset:-250]\n[00:01.00]一\n[00:01.00]one\n[00:20.00]二", "lrc");
        Check(lrc.Lines.Count == 2 && lrc.Lines[0].Translation == "one" && lrc.Lines[0].EndMs == 20000 && lrc.FindLine(1200) == -1 && lrc.FindLine(1250) == 0, "LRC timing/translation/binary lookup");
        var yrc = LyricsParser.Parse("[1000,2000](1000,500,0)你(1500,1500,0)好", "yrc");
        Check(yrc.Lines[0].Text == "你好" && yrc.Lines[0].Words[1].EndMs == 3000, "YRC words");
        var enhanced = LyricsParser.Parse("[00:01.00]<00:01.00>Hello <00:02.00>world<00:03.00>", "lrc");
        Check(enhanced.Lines[0].Words.Count == 2 && enhanced.Lines[0].Text == "Hello world", "enhanced LRC");
        var ttml = LyricsParser.Parse("<tt xmlns:ttm='http://www.w3.org/ns/ttml#metadata'><body><p begin='1s' end='4s'><span begin='1s' end='2s'>你好</span><span ttm:role='x-translation'>Hello</span><span ttm:role='x-roman'>ni hao</span></p></body></tt>", "ttml");
        Check(ttml.Lines[0].Translation == "Hello" && ttml.Lines[0].Romanization == "ni hao" && ttml.Lines[0].Words.Count == 1, "TTML role/timing");
        var srt = LyricsParser.Parse("1\n00:00:01,000 --> 00:00:03,000\n你好\n\n2\n00:00:04,000 --> 00:00:06,000\n世界", "srt");
        Check(srt.Lines.Count == 2 && srt.Lines[1].StartMs == 4000, "subtitle cues");
        Check(!LyricsParser.Parse("你好\n世界", "txt").IsSynced, "plain text");
        try { LyricsParser.Parse(new string('a', LyricsParser.MaxCharacters + 1), "txt"); throw new Exception("missing bound"); } catch (InvalidDataException) { }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        try { LyricsParser.Parse("[00:01]hello", "lrc", canceled.Token); throw new Exception("missing cancellation"); } catch (OperationCanceledException) { }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
