using System.Text.Json;
using Echo.Core.Contracts;
using Echo.Dsp;

// Production Core service, isolated storage. Never opens the user's DSP library for UI verification.
internal sealed class DspFixtureResources : IDspResourceClient, IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "echo-dsp-" + Guid.NewGuid().ToString("N"));
    private readonly DspResourceService service;
    public DspResourceService Service => service;
    public DspFixtureResources() => service = new(Root);
    public Task<JsonElement> ExecuteAsync(DspResourceOperation operation, string? id = null, string? name = null,
        string? path = null, string? query = null, int offset = 0, JsonElement? state = null, bool refresh = false, CancellationToken token = default)
        => service.ExecuteAsync(new(CoreDspProtocol.Version, Guid.NewGuid().ToString("N"), operation, id, name, path, query, offset, state, refresh), token);
    public void Dispose() { service.Dispose(); Directory.Delete(Root, true); }
}
