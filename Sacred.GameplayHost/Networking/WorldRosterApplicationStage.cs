using Sacred.Network.Admission;

namespace Sacred.GameplayHost.Networking;

/// <summary>Applied roster plus ordered pending service effects. This does not
/// perform UI delivery or complete the remaining admission handler.</summary>
public sealed record WorldRosterApplicationStage(GoldPlayerRoster Roster,
    IReadOnlyList<WorldRosterAppliedRow> Rows, IReadOnlyList<byte> JoinNotices)
{
    public int ListResetRequests => 2;
    public int ListFinalizeRequests => 2;
    public bool RefreshRequested => true;
    public bool RequiresAcceptedContinuation => true;
}
