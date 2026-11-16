namespace Sacred.GameplayHost.Networking;

/// <summary>Compared native row effects; presentation and profile-event delivery
/// remain explicit consumers. Identifier conversion is supplied by the caller.</summary>
public sealed record WorldRosterAppliedRow(byte Slot, string Name, string Identifier, bool LowBandwidth, bool HasProfile);
