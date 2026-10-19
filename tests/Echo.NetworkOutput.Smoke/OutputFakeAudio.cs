using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

internal static class OutputFakeAudio
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Contains("-list")) { Console.WriteLine("0\tOutput smoke device\t48000\t1\t0\t0\t0\tsmoke-device"); return; }
        File.WriteAllText(Environment.GetEnvironmentVariable("ECHO_OUTPUT_SMOKE_PID")!, Environment.ProcessId.ToString());
        Console.WriteLine("{\"ready\":true,\"protocolVersion\":1,\"backendContractVersion\":2,\"capabilities\":{\"hostOwnedLocalPlaybackV1\":true}}");
        long operation = 0; var verified = false;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line); var request = document.RootElement;
            var method = request.GetProperty("method").GetString(); var argument = request.TryGetProperty("params", out var body) ? body : default;
            if (argument.ValueKind == JsonValueKind.Array) argument = argument[0];
            var result = "true";
            try
            {
                switch (method)
                {
                    case "queue.set":
                        foreach (var item in argument.GetProperty("items").EnumerateArray())
                            if (item.GetProperty("source").GetProperty("kind").GetString() != "http" || !item.GetProperty("filePath").GetString()!.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)) throw new Exception("Unsafe native source");
                        result = "{\"queueRevision\":" + argument.GetProperty("revision").GetInt64() + "}"; break;
                    case "audio.sessionBegin": result = "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"channels\":2,\"deviceName\":\"simulated remote output\"}}"; break;
                    case "device.configure": result = "{\"accepted\":true,\"sampleRate\":48000,\"channels\":2,\"deviceName\":\"simulated remote output\"}"; break;
                    case "audio.openFile": throw new Exception("Core local path crossed output boundary");
                    case "audio.openSource":
                        await VerifyAsync(argument.GetProperty("source").GetProperty("uri").GetString()!); verified = true;
                        result = "{\"operationId\":" + ++operation + ",\"sampleRate\":48000,\"durationSeconds\":60}"; break;
                    case "audio.seek": result = "{\"operationId\":" + ++operation + "}"; break;
                    case "playbackRate.configure": result = "{\"rate\":1,\"mode\":\"speed\"}"; break;
                    case "audio.play": if (!verified) throw new Exception("Media was not verified"); break;
                }
                Console.WriteLine("{\"id\":" + request.GetProperty("id").GetInt64() + ",\"result\":" + result + "}");
                if (method == "audio.play") Event("audio.started", operation, 1);
                if (method == "audio.resume")
                {
                    Event("audio.position", operation, 100);
                    await Task.Delay(250); Event("audio.position", operation, 200);
                }
                if (method == "rpc.shutdown") return;
            }
            catch (Exception) { Console.WriteLine("{\"id\":" + request.GetProperty("id").GetInt64() + ",\"error\":{\"code\":-1,\"message\":\"Output smoke media verification failed\"}}"); }
        }
    }
    private static void Event(string method, long operation, int frames) => Console.WriteLine("{\"method\":\"" + method + "\",\"params\":{\"operationId\":" + operation + ",\"framesPlayed\":" + frames + "}}");
    private static async Task VerifyAsync(string url)
    {
        if (!url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)) throw new Exception("Not a loopback proxy");
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        using var head = await client.SendAsync(new(HttpMethod.Head, url));
        if (head.Content.Headers.ContentLength is not > 1024 * 1024) throw new Exception("Missing bounded media length");
        using var range = new HttpRequestMessage(HttpMethod.Get, url); range.Headers.Range = new RangeHeaderValue(64, 4095);
        using var partial = await client.SendAsync(range);
        var bytes = await partial.Content.ReadAsByteArrayAsync();
        if ((int)partial.StatusCode != 206 || bytes.Length != 4096 - 64 || bytes[0] != (byte)(((64 - 44) * 31 + 7) & 255)) throw new Exception("Incorrect protected Range stream");
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[4096]; int read;
        while ((read = await stream.ReadAsync(buffer)) != 0) hash.AppendData(buffer, 0, read);
        if (Convert.ToHexString(hash.GetHashAndReset()) != Environment.GetEnvironmentVariable("ECHO_OUTPUT_SMOKE_HASH")) throw new Exception("Protected file bytes changed");
    }
}
