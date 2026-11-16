namespace Sacred.GameplayHost.Networking;

/// <summary>Composed original admission decisions, not session/actor readiness.
/// A late rejection retains the roster mutations made before its guard.</summary>
public sealed record WorldAdmissionPlanResult(WorldAdmissionControllerPrefixResult Prefix,
    WorldAdmissionRosterStage? StagedRoster, WorldRosterApplicationStage? AppliedRoster,
    byte Flag2, bool LateRejected, WorldAdmissionRequests Requests)
{
    public bool RequiresServiceDelivery => true;
    public bool RequiresActorBinding => !Prefix.Rejected && !LateRejected;
}
