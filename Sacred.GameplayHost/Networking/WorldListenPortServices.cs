namespace Sacred.GameplayHost.Networking;

/// <summary>Original settings lookup/write and numeric conversion boundaries.
/// ReadValue returns the stored byte count, including any embedded NUL. Parsing
/// may use the compared CRT model with an established current class table.</summary>
public sealed record WorldListenPortServices(Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> ReadValue,
    Action<ReadOnlyMemory<byte>, uint> WriteDefault, Func<ReadOnlyMemory<byte>, int> ParseDecimal);
