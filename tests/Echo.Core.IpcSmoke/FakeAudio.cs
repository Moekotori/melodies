using System.Text.Json;

internal static class FakeAudio
{
    // A real separate executable speaking the existing native control protocol. No device or audio-quality claims.
    public static async Task RunAsync()
    {
        Console.WriteLine("{\"ready\":true,\"protocolVersion\":1,\"backendContractVersion\":2,\"capabilities\":{\"hostOwnedLocalPlaybackV1\":true,\"runtimeDeviceConfigureV1\":true,\"nativeDspV1\":true},\"hostIdentity\":{\"schemaVersion\":1,\"imageRole\":\"echo-audio-host\",\"buildId\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"sourceDigest\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"protocolVersion\":1,\"backendContractVersion\":2}}");
        var operation = 0;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            using var document = JsonDocument.Parse(line);
            var request = document.RootElement;
            var method = request.GetProperty("method").GetString();
            var parameters = request.TryGetProperty("params", out var value) ? value : default;
            var result = method switch
            {
                "queue.set" => "{\"queueRevision\":" + parameters.GetProperty("revision").GetInt64() + "}",
                "audio.sessionBegin" => "{\"accepted\":true,\"sessionId\":1,\"ready\":{\"sampleRate\":48000,\"deviceName\":\"simulated IPC output\"}}",
                "device.configure" => "{\"accepted\":true,\"sampleRate\":48000,\"channels\":2,\"deviceName\":\"simulated IPC output\"}",
                "audio.openFile" => "{\"operationId\":" + ++operation + ",\"sampleRate\":48000,\"durationSeconds\":10}",
                "audio.seek" => "{\"operationId\":" + ++operation + "}",
                "playbackRate.configure" => "{\"rate\":1,\"mode\":\"speed\"}",
                _ => "true"
            };
            Console.WriteLine("{\"id\":" + request.GetProperty("id").GetInt64() + ",\"result\":" + result + "}");
            if (method == "audio.play") Console.WriteLine("{\"method\":\"audio.started\",\"params\":{\"operationId\":" + operation + "}}");
            if (method == "rpc.shutdown") return;
        }
    }
}
