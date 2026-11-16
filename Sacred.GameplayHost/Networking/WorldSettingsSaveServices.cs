namespace Sacred.GameplayHost.Networking;

/// <summary>Open and formatted write/close boundaries. The original saver ignores
/// individual write and close results and continues to the next live entry.</summary>
public sealed record WorldSettingsSaveServices(Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>, uint> Open,
    Func<uint, ReadOnlyMemory<byte>, int> Write, Func<uint, int> Close);
