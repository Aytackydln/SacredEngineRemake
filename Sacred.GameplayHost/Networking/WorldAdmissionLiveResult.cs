namespace Sacred.GameplayHost.Networking;

/// <summary>Fields after the synchronous outer branch returns. Completion of
/// supplied services does not establish actor registration, binding or readiness.</summary>
public sealed record WorldAdmissionLiveResult(bool PrefixRejected, bool LateRejected,
    byte PendingByte3, byte HostByte1, WorldController117Identity Identity);
