using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Local controller-instance storage plus current connection and
/// supplied backend/UI/process services. QueryBackend is the 0x7D5B40 predicate;
/// BackendCommand includes 0x7D5F50's pump and current-backend dispatch.</summary>
public sealed record WorldController117Services(WorldControlMemory Controller,
    Func<WorldConnectionClearServices?> ReadConnection, WorldHostedServerProcessServices HostedProcess,
    Action ClearPendingVector, Func<bool> QueryBackend,
    Action<WorldConnectionClearServices, uint> BackendCommand, Action<GoldLobbyUiRequest> Ui,
    Action<string> ChatResource);
