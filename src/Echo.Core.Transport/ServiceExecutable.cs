namespace Echo.Core.Transport;

public static class ServiceExecutable
{
    public static string Resolve(string directory, string name, string variable)
    {
        if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } configured) return Path.GetFullPath(configured);
        var executable = Path.Combine(directory, name + (OperatingSystem.IsWindows() ? ".exe" : ""));
        return File.Exists(executable) ? executable : Path.Combine(directory, name + ".dll");
    }
}
