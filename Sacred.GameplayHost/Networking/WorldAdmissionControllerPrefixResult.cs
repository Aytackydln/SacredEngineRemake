namespace Sacred.GameplayHost.Networking;

/// <summary>Only the compared prefix decision. Acceptance still needs the
/// original roster/UI/upload continuation before session/actor readiness.</summary>
public sealed record WorldAdmissionControllerPrefixResult(WorldAdmissionControllerState State, bool Rejected)
{
    public bool RequiresAcceptedContinuation => !Rejected;
}
