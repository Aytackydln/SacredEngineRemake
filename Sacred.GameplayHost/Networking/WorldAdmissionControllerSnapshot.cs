using Sacred.Network.Admission;

namespace Sacred.GameplayHost.Networking;

/// <summary>One coherent controller/roster snapshot. Constructor activity alone
/// cannot establish the separate multiplayer global. This is not actor readiness.</summary>
public sealed record WorldAdmissionControllerSnapshot(WorldAdmissionControllerState State, GoldPlayerRoster Roster,
    WorldAdmissionControllerGates Gates, WorldAdmissionControllerServiceFlags ServiceFlags, byte? MultiplayerByte)
{
    public WorldAdmissionContinuationInputs CreateInputs(bool prefixQuery, bool laterQuery, WorldAdmissionRosterInputs rosterInputs)
    {
        if (MultiplayerByte is not { } multiplayer)
            throw new NotSupportedException("Controller construction does not establish the multiplayer global.");
        return new(prefixQuery, laterQuery, Gates.Flag9 != 0, Gates.Flag28 != 0,
            ServiceFlags.Byte381B != 0, Gates.Flag2, rosterInputs) { Multiplayer = multiplayer != 0 };
    }
}
