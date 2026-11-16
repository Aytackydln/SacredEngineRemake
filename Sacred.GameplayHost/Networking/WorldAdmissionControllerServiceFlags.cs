namespace Sacred.GameplayHost.Networking;

/// <summary>Current raw service bytes. Initialization preserves the middle four;
/// constructor initialization and later mode/UI writes have separate provenance.</summary>
public sealed record WorldAdmissionControllerServiceFlags(byte Byte3818, byte Byte3819, byte Byte381A,
    byte Byte381B, byte Byte381C, byte Byte381D);
