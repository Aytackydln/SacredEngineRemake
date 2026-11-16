namespace Sacred.GameplayHost.Networking;

/// <summary>Compared admission-prefix controller bytes/words. A local reference
/// is an identity candidate, not proof of a live Creature registry binding.
/// HostToken is the legacy name for the +0x24 hosted-server window token.</summary>
public sealed record WorldAdmissionControllerState(byte PendingByte3, byte HostByte1,
    uint LocalReference, uint HostToken, uint ConnectionLocalReference);
