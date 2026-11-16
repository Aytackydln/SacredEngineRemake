namespace Sacred.GameplayHost.Networking;

/// <summary>Captured native address-manager storage. ReadConfiguredAddress owns
/// the original settings getter; strings/logging are synchronous service bodies.
/// This is the network-adapter list, not a world-actor/reference registry.</summary>
public sealed record WorldNetworkAddressServices(WorldControlMemory Manager, Func<ReadOnlyMemory<byte>> ReadConfiguredAddress,
    Func<ReadOnlyMemory<byte>, uint> ParseAddress, Action<uint, ReadOnlyMemory<byte>> WriteString,
    Action<string, ReadOnlyMemory<byte>> Log);
