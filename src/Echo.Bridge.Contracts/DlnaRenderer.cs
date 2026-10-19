namespace Echo.Bridge;

public sealed record DlnaService(string ServiceType, string ControlUrl);
public sealed record DlnaRenderer(string Name, string Model, string Address, DlnaService AvTransport, DlnaService? Rendering);
