using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Synchronous service boundaries for the complete Gold 0x11 branch.
/// Controller storage is the captured instance; connection storage is current.
/// AcceptedRoster owns 0x7E1332..0x7E15B4, including prefix/staging writes,
/// string/identifier work and the entire roster consumer. It must return only
/// after per-row UI/profile/refresh work finishes. A prepared plan is insufficient.
/// After real staging, DeliverLiveRoster supplies the compared consumer on this receipt.
/// Storage accessors must be pure. No native OS or Engine bindings are installed.</summary>
public sealed record WorldAdmissionLiveServices(WorldControlMemory Controller,
    Func<WorldControlMemory?> ReadConnection, Action ResetResource, Func<bool> QueryConnection,
    Action<GoldAdmissionEvent> AcceptedRoster, Action<GoldLobbyUiRequest> Ui,
    Action DispatchController117, Action FinalizeConnection, Action<string> ChatResource);
