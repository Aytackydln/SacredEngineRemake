namespace Sacred.GameplayHost.Networking;

/// <summary>Current raw controller bytes, independent of incoming host flags.</summary>
public sealed record WorldAdmissionControllerGates(byte ActiveByte0, byte Flag2, byte Flag9, byte Flag28);
