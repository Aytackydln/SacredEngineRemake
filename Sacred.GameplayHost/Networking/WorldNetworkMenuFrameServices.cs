using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Original full 0x70B500 caller. Child update owns its virtual service,
/// including the special page's two trailing zero arguments. UI owns immediate
/// message construction/delivery; MenuAddress is the captured caller identity.</summary>
public sealed record WorldNetworkMenuFrameServices(WorldNetworkMenuServices Mode, uint MenuAddress,
    Action<uint> BaseUpdate, Action<uint, uint, bool> UpdateChild, Action<GoldLobbyUiRequest> Ui);
