using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Ordered service boundaries in the compared 0x11 handler. These are
/// requests, not evidence that the native services have been implemented.</summary>
public abstract record WorldAdmissionEffect
{
    public sealed record ResetService : WorldAdmissionEffect;
    /// <summary>The complete roster service, including its ordered UI/profile/
    /// refresh work. A consumer must retain its own progress until all of it completes.</summary>
    public sealed record Roster(WorldRosterApplicationStage Application) : WorldAdmissionEffect;
    public sealed record Ui(GoldLobbyUiRequest Request) : WorldAdmissionEffect;
    /// <summary>Local family-10 event dispatched through 0x7D8950. This is
    /// distinct from sending a protocol packet with numeric opcode 0x117.</summary>
    public sealed record DispatchController117 : WorldAdmissionEffect;
    public sealed record FinalizeConnection : WorldAdmissionEffect;
    public sealed record Chat(string ResourceKey) : WorldAdmissionEffect;
}
