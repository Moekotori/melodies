using System.Net;
using System.Text;
using Echo.Application.Radio;
using Echo.Contracts.Radio;
using Echo.Infrastructure.Radio;

internal static class RadioFeatureSmoke
{
    public static async Task RunAsync(CancellationToken token)
    {
        Check(IcyTitle.Read("StreamTitle='A - B';") == "A - B", "ICY title");
        Check(IcyTitle.Read("StreamTitle='&#31348;';") == "穴", "ICY entity");
        var programme = RadioProgramme.Split("A - B", "Station");
        Check(programme.Title == "B" && programme.Artist == "A", "Programme split");
        Check(RadioProgramme.Split("Station", "Station").Title.Length == 0, "Programme ignores the station name");
        Check(RadioProgramme.Split("直播", "Station").Title.Length == 0, "Programme ignores a placeholder");
        var playlist = RadioPlaylist.Parse("[playlist]\nNumberOfEntries=1\nFile1=https://radio.test/a\nTitle1=Night\n");
        Check(playlist.Count == 1 && playlist[0].Name == "Night" && playlist[0].Url == "https://radio.test/a", "PLS import");
        var loose = RadioPlaylist.Parse("listen https://radio.test/b extra");
        Check(loose.Count == 1 && loose[0].Url == "https://radio.test/b", "Loose URL import");
        var probed = RadioCatalog.ApplyProbe(new("id", "Mine", "https://radio.test/a", Codec: "—"),
            new("https://radio.test/audio", "AAC", "Server", Genre: "jazz", BitrateKbps: 128, Homepage: "https://radio.test/"));
        Check(probed.Name == "Mine" && probed.Codec == "AAC" && probed.Genre == "jazz" && probed.BitrateKbps == 128 && probed.Homepage == "https://radio.test/", "Probe keeps a custom name");

        var message = ServiceMessage();
        var targets = DnsSrvQuery.ReadTargets(message);
        Check(targets.Count == 1 && targets[0] == "de1.api.radio-browser.info", "SRV target");
        Check(RadioDirectoryParser.IsOfficialHost("de1.api.radio-browser.info") && !RadioDirectoryParser.IsOfficialHost("evil.example"), "Official directory host");

        var calls = 0;
        using var directory = new RadioDirectoryClient(new DirectoryFixture(() => calls++), _ => Task.FromResult<IReadOnlyList<string>>(["de1.api.radio-browser.info"]));
        var found = await directory.SearchAsync(new("night", "jp", "", "", 0), token);
        Check(calls == 1 && found.Error is null && found.Stations.Count == 1 && found.Stations[0].Name == "Night" && found.Stations[0].Codec == "MP3", "Directory filters broken and credentialed stations");
        using var blocked = new RadioDirectoryClient(new DirectoryFixture(() => calls++), _ => Task.FromResult<IReadOnlyList<string>>(["evil.example"]));
        var unavailable = await blocked.SearchAsync(new("", "", "", "", 0), token);
        Check(unavailable.Error == "unavailable" && calls == 1, "Unofficial directory host is not contacted");

        using var monitor = new IcyMetadataMonitor(new IcyFixture());
        var seen = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += () => { if (monitor.StreamTitle is not null) seen.TrySetResult(monitor.StreamTitle); };
        monitor.Follow("https://radio.test/live", false);
        var title = await seen.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        Check(title == "A - B", "ICY monitor");
        monitor.Follow(null, true);
        Check(monitor.StreamTitle == "A - B" && monitor.StreamUrl == "https://radio.test/live", "Paused radio keeps the programme");
        monitor.Follow(null, false);
        Check(monitor.StreamTitle is null && monitor.StreamUrl is null, "Stopping radio clears the programme");
        Console.WriteLine("PASS radio directory, ICY programme, playlist import and probe merge");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static byte[] ServiceMessage()
    {
        var question = Name("_api._tcp.radio-browser.info");
        var target = Name("de1.api.radio-browser.info");
        var answer = 12 + question.Length + 4;
        var bytes = new byte[answer + 12 + 6 + target.Length];
        bytes[2] = 0x81; bytes[3] = 0x80; bytes[5] = 1; bytes[7] = 1;
        question.CopyTo(bytes, 12);
        bytes[12 + question.Length + 1] = 33;
        bytes[12 + question.Length + 3] = 1;
        bytes[answer] = 0xC0; bytes[answer + 1] = 12;
        bytes[answer + 3] = 33; bytes[answer + 5] = 1;
        var dataLength = 6 + target.Length;
        bytes[answer + 10] = (byte)(dataLength >> 8); bytes[answer + 11] = (byte)dataLength;
        target.CopyTo(bytes, answer + 18);
        return bytes;
    }

    private static byte[] Name(string value)
    {
        var labels = value.Split('.');
        var bytes = new byte[labels.Sum(label => label.Length + 1) + 1];
        var offset = 0;
        foreach (var label in labels)
        {
            bytes[offset++] = (byte)label.Length;
            Encoding.ASCII.GetBytes(label).CopyTo(bytes, offset);
            offset += label.Length;
        }
        return bytes;
    }

    private sealed class DirectoryFixture(Action hit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            hit();
            var json = """
                [{"stationuuid":"11111111-1111-1111-1111-111111111111","name":"Night","url_resolved":"https://radio.test/live","countrycode":"JP","language":"japanese","tags":"jazz","codec":"MP3","bitrate":128,"lastcheckok":1},
                {"stationuuid":"22222222-2222-2222-2222-222222222222","name":"Broken","url":"https://radio.test/x","lastcheckok":0},
                {"stationuuid":"33333333-3333-3333-3333-333333333333","name":"Secret","url":"https://user:pass@radio.test/x","lastcheckok":1}]
                """;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(json, Encoding.UTF8, "application/json") };
            return Task.FromResult(response);
        }
    }

    private sealed class IcyFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var meta = new byte[32];
            Encoding.ASCII.GetBytes("StreamTitle='A - B';").CopyTo(meta, 0);
            var body = new byte[16 + 1 + meta.Length];
            body[16] = 2;
            meta.CopyTo(body, 17);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(body) };
            response.Headers.TryAddWithoutValidation("icy-metaint", "16");
            return Task.FromResult(response);
        }
    }
}
