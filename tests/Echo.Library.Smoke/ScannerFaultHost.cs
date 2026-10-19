using System.Text.Json;

internal static class ScannerFaultHost
{
    public const string StateVariable = "ECHO_SCAN_SMOKE_FAULT_STATE";
    public const string ModeVariable = "ECHO_SCAN_SMOKE_FAULT_MODE";

    public static bool TryRun()
    {
        var state = Environment.GetEnvironmentVariable(StateVariable);
        if (state is null) return false;
        var mode = Environment.GetEnvironmentVariable(ModeVariable);
        Console.WriteLine(mode == "protocol" ? "{\"type\":\"capabilities\",\"protocolVersion\":999}"
            : "{\"type\":\"capabilities\",\"protocolVersion\":3}");
        Console.WriteLine("{\"type\":\"ready\"}");
        while (Console.ReadLine() is { } message)
        {
            using var request = JsonDocument.Parse(message);
            var attempts = File.Exists(state) ? int.Parse(File.ReadAllText(state), System.Globalization.CultureInfo.InvariantCulture) : 0;
            File.WriteAllText(state, (++attempts).ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (mode == "crash" || mode == "recover" && attempts <= 3) Environment.Exit(13);
            var id = request.RootElement.GetProperty("requestId").GetString();
            Console.WriteLine("{\"type\":\"metadata\",\"requestId\":\"" + id
                + "\",\"result\":{\"fields\":{\"title\":\"Healthy\",\"artist\":\"Artist\",\"duration\":4,\"sampleRate\":48000,\"bitDepth\":16,\"codec\":\"PCM\"}}}");
        }
        return true;
    }
}
