using System.Text.Json.Serialization;
using Echo.Core.Contracts;

namespace Echo.Core.Transport;

// Envelope serialization is independent from the business RPC context, so Output does not root library JSON models.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CoreMessage))]
[JsonSerializable(typeof(CoreHello))]
[JsonSerializable(typeof(CoreWelcome))]
[JsonSerializable(typeof(CoreEndpointInfo))]
public partial class CoreWireJson : JsonSerializerContext;
