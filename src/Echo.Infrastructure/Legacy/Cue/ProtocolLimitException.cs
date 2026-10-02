namespace Echo.Core.Protocol;

// Extracted from the legacy FrameCodec; CUE code uses only this exception.
public sealed class ProtocolLimitException(string message) : Exception(message);
