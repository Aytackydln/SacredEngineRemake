using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Ordered pending UI/chat requests and service boundaries. Preparing
/// requests does not deliver UI, send 0x117 or execute the opaque finalizer.</summary>
public sealed record WorldAdmissionRequests(IReadOnlyList<GoldLobbyUiRequest> Ui,
    IReadOnlyList<string> ChatResourceKeys, int Native117Requests, bool FinalizerRequested);
