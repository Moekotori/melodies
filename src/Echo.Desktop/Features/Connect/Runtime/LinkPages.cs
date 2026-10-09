using System.Net;
using System.Text;

namespace Echo.Desktop.Features.Connect.Runtime;

internal static class LinkPages
{
    public static byte[] Pair(string codeHint) => Page("配对 ECHO",
        "<h1>配对这台 ECHO</h1><p>输入电脑上显示的 6 位配对码。码会过期，连续猜错会锁定一分钟。</p>"
        + "<form method=\"post\" action=\"/echo-link/pair\"><input name=\"code\" inputmode=\"numeric\" maxlength=\"6\" autocomplete=\"one-time-code\" />"
        + "<button class=\"main\" type=\"submit\">配对</button></form><p>" + WebUtility.HtmlEncode(codeHint) + "</p>");

    public static byte[] Remote(string token, string title, string artist, string status, int volume)
    {
        var body = new StringBuilder();
        body.Append("<p class=\"kicker\">正在播放</p><h1>").Append(WebUtility.HtmlEncode(title)).Append("</h1><p>")
            .Append(WebUtility.HtmlEncode(artist.Length == 0 ? " " : artist)).Append("</p><p class=\"status\">")
            .Append(WebUtility.HtmlEncode(Status(status))).Append(" · 音量 ")
            .Append(volume.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("</p>");
        body.Append(Form(token, "toggle", "播放 / 暂停", true));
        body.Append("<div class=\"row\">").Append(Form(token, "previous", "上一首", false)).Append(Form(token, "next", "下一首", false)).Append("</div>");
        body.Append(Form(token, "stop", "停止", false));
        body.Append("<div class=\"row\">").Append(Form(token, "volume:20", "20", false)).Append(Form(token, "volume:60", "60", false)).Append(Form(token, "volume:100", "100", false)).Append("</div>");
        return Page("ECHO Link", body.ToString());
    }

    private static string Status(string status) => status switch
    {
        "Playing" or "Loading" => "正在播放",
        "Paused" => "已暂停",
        "Ended" => "已结束",
        "Faulted" => "播放出错",
        _ => "已停止"
    };

    private static string Form(string token, string action, string label, bool main)
        => "<form method=\"post\" action=\"/echo-link/command\"><input type=\"hidden\" name=\"token\" value=\"" + WebUtility.HtmlEncode(token)
           + "\" /><input type=\"hidden\" name=\"action\" value=\"" + WebUtility.HtmlEncode(action) + "\" /><button"
           + (main ? " class=\"main\"" : "") + " type=\"submit\">" + WebUtility.HtmlEncode(label) + "</button></form>";

    private static byte[] Page(string title, string body)
        => Encoding.UTF8.GetBytes("<!doctype html><html><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>"
            + WebUtility.HtmlEncode(title) + "</title><style>"
            + "body{margin:0;background:#161616;color:#f3f3f3;font-family:-apple-system,BlinkMacSystemFont,sans-serif}"
            + "main{max-width:420px;margin:auto;padding:32px 20px}h1{font-size:28px;line-height:1.25;margin:0}"
            + ".kicker{letter-spacing:.12em;font-size:11px;color:#9a9a9a}.status{color:#c8c8c8}"
            + "form{margin:0}.row{display:flex;gap:8px}.row form{flex:1}"
            + "button,input{width:100%;box-sizing:border-box;margin:8px 0 0;padding:14px;border:0;border-radius:12px;background:#2a2a2a;color:#fff;font:inherit}"
            + "button.main{background:#f3f3f3;color:#161616}input{font-size:24px;letter-spacing:.3em;text-align:center}"
            + "</style></head><body><main>" + body + "</main></body></html>");
}
