namespace Sacred.GameplayHost.Networking;

/// <summary>Pure storage adapters for an established native-layout controller
/// or connection instance. Service callbacks are separate and may reenter.</summary>
public sealed record WorldControlMemory(Func<uint, byte> ReadByte, Action<uint, byte> WriteByte,
    Func<uint, uint> ReadWord, Action<uint, uint> WriteWord, Action<uint, ushort> WriteHalfWord);
