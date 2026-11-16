using Sacred.Network.Admission;

namespace Sacred.GameplayHost.Networking;

/// <summary>Compared input to native 0x7E4C20, not credit for that consumer or
/// the remainder of admission. Byte names retain their runtime offsets.</summary>
public sealed record WorldAdmissionRosterStage(GoldPlayerRoster Roster, byte Flag48, byte Flag3819)
{
    public bool RequiresAcceptedContinuation => true;
}
