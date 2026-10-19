using Echo.Application.Lyrics;
using Echo.Contracts.Lyrics;
using Echo.Infrastructure.Lyrics;

static class FormatCoverageSmoke
{
    public static async Task Run(string root, CancellationToken token)
    {
        var samples = new Dictionary<string, string>
        {
            ["ass"] = "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.25,0:00:03.00,Default,,0,0,0,,{\\k50}Hello {\\kf100}world",
            ["ssa"] = "[Events]\nFormat: Marked, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: Marked=0,0:00:01.25,0:00:03.00,Default,,0,0,0,,Hello, world",
            ["trc"] = "[00:01.25]<500>Hello <1000>world",
            ["lrcx"] = "[00:01.25]Hello world\n[00:01.25][tt]<0,0><500,6><1500,11>\n[00:01.25][tr:zh-Hans]你好世界\n[00:01.25][ro]<hello,0,5>",
            ["snc"] = "⑩00000125⑿Hello\nworld",
            ["vtt"] = "WEBVTT\n\nNOTE hidden\n00:01.000 --> 00:02.000\nignore\n\n00:01.250 --> 00:03.000 align:start\n<c>Hello </c><00:01.750><b>world</b>",
            ["echo-lyrics.json"] = """{"schema":"echo.lyrics","version":1,"kind":"synced","offsetMs":150,"lines":[{"timeMs":1250,"endMs":3000,"text":"Hello world","translation":"你好世界","romanization":"hello world","words":[{"text":"Hello ","startMs":1250,"endMs":1750},{"text":"world","startMs":1750,"endMs":2750}]}]}"""
        };
        await using var source = new LocalLyricsSource();
        foreach (var (format, text) in samples)
        {
            var parsed = LyricsParser.Parse(text, format);
            Check(parsed.IsSynced && parsed.Lines.Count == 1 && parsed.Lines[0].StartMs == 1250, format + " synchronized time");
            Check(parsed.Lines[0].Text == (format == "ssa" ? "Hello, world" : "Hello world"), format + " clean text");
            if (format is not ("snc" or "ssa"))
                Check(parsed.Lines[0].Words.Count == 2 && parsed.Lines[0].Words[1].StartMs == 1750, format + " word timing");
            await File.WriteAllTextAsync(Path.Combine(root, "format-" + format + "." + format), text, token);
            var local = await source.ReadAsync(Path.Combine(root, "format-" + format + ".flac"), null, token);
            Check(local is not null && LyricsParser.Parse(local).Lines[0].StartMs == 1250, format + " automatic sidecar");
        }
        var lrcx = LyricsParser.Parse(samples["lrcx"], "lrc");
        Check(lrcx.Lines[0].Translation == "你好世界" && lrcx.Lines[0].Romanization == "hello world", "LRCX attachments detected in LRC");
        var graphemes = LyricsParser.Parse("[00:01]A👨‍👩‍👧‍👦B\n[00:01][tt]<0,0><200,1><600,2><900,3>", "lrcx");
        Check(graphemes.Lines[0].Words[1].Text == "👨‍👩‍👧‍👦" && graphemes.Lines[0].Words[2].Text == "B", "LRCX uses grapheme indices");
        var bracket = LyricsParser.Parse("[00:01]Hello [00:02]world[00:03]\n[00:04][00:08]Repeat", "lrc");
        Check(bracket.Lines.Count == 3 && bracket.Lines[0].Words.Count == 2 && bracket.Lines[1].Text == "Repeat", "bracket word timing versus repeated line stamps");
        var yrc = LyricsParser.Parse("[10000,1000](0,400,0)你(400,600,0)好", "yrc");
        Check(yrc.Lines[0].Words[1].StartMs == 10400, "relative YRC words");
        var primary = LyricsParser.Parse("[00:01]译文\n[00:01]<00:01>原<00:02>文<00:03>", "lrc");
        Check(primary.Lines[0].Text == "原文" && primary.Lines[0].Translation == "译文", "timed original wins same-time translation");
        var final = LyricsParser.Parse("[00:01]<00:01>long\n[00:08]next", "lrc");
        Check(final.Lines[0].Words[0].EndMs == 8000, "unknown final word end follows line boundary");
        var ttml = LyricsParser.Parse("""
            <tt xmlns:ttm="http://www.w3.org/ns/ttml#metadata" xmlns:ttp="http://www.w3.org/ns/ttml#parameter" ttp:frameRate="25">
            <body><div><p begin="10s" dur="2s"><span><span begin="10s" dur="500ms">你&amp;</span><span begin="10.5s" end="00:00:11:00">好</span></span><br/><span>世界</span><span ttm:role="x-translation"><span>Hello</span></span></p></div></body></tt>
            """, "ttml");
        Check(ttml.Lines[0].Text == "你&好\n世界" && ttml.Lines[0].Translation == "Hello"
            && ttml.Lines[0].Words.Count == 2 && ttml.Lines[0].Words[1].EndMs == 11000 && ttml.Lines[0].EndMs == 12000,
            "nested TTML leaf timing, duration, frame clocks, entities and secondary text");
        var relative = LyricsParser.Parse("<tt><body><p begin='10s' end='12s'><span begin='0s' dur='1s'>相对</span></p></body></tt>", "ttml");
        Check(relative.Lines[0].Words[0].StartMs == 10000 && relative.Lines[0].Words[0].EndMs == 11000, "relative TTML offsets");
        var ass = LyricsParser.Parse(samples["ass"] + "\nComment: 0,0:00:01.00,0:00:02.00,D,,0,0,0,,Hidden\nDialogue: 0,0:00:04.00,0:00:06.00,D,,0,0,0,,{\\p1}m 0 0 l 50 50{\\p0}Hi, again", "ass");
        Check(ass.Lines.Count == 2 && ass.Lines[1].Text == "Hi, again", "ASS drawing/comment exclusion and comma preservation");
        Check(LyricsParser.Parse("1\n00:99:01,000 --> 00:99:03,000\nbad\n\n2\n00:00:04,000 --> 00:00:03,000\nreverse", "srt").Lines.Count == 0, "invalid subtitle times are discarded");
        var plain = LyricsParser.Parse("""{"schema":"echo.lyrics","version":1,"kind":"plain","lines":[{"timeMs":-1,"text":"one"},{"timeMs":-1,"text":"two"}]}""", "json");
        Check(!plain.IsSynced && plain.Lines.Count == 2, "portable plain line order");
        Reject("{\"schema\":\"other\",\"version\":1,\"lines\":[]}", "json");
        Reject(samples["echo-lyrics.json"].Replace("\"startMs\":1750", "\"startMs\":-1"), "json");
        Reject("<tt>" + string.Concat(Enumerable.Repeat("<span>", 40)) + "text" + string.Concat(Enumerable.Repeat("</span>", 40)) + "</tt>", "ttml");
        Reject("[1000,5000]" + string.Concat(Enumerable.Repeat("(1000,1,0)a", LyricsParser.MaxWordsPerLine + 1)), "yrc");
        Check(LyricsFileFormats.DialogPatterns.Contains("*.echo-lyrics.json"), "schema-specific file picker");
        Console.WriteLine("PASS additional lyric formats: local discovery, word timing, Unicode, TTML nesting, subtitle safety and portable JSON limits.");
    }
    static void Reject(string text, string format)
    {
        try { LyricsParser.Parse(text, format); throw new Exception("accepted invalid " + format); }
        catch (InvalidDataException) { }
    }
    static void Check(bool value, string name) { if (!value) throw new Exception(name); }
}
