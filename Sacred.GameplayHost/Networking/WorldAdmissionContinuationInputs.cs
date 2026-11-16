namespace Sacred.GameplayHost.Networking;

/// <summary>Explicit query/field snapshots for the eager admission projection.
/// These values do not establish field state after synchronous service reentry.</summary>
public sealed record WorldAdmissionContinuationInputs(bool PrefixConnectionQuery, bool LaterConnectionQuery,
    bool ControllerFlag9, bool ControllerFlag28, bool ControllerFlag381B, byte PreviousFlag2,
    WorldAdmissionRosterInputs RosterInputs)
{
    public bool? Multiplayer { get; init; }
}
