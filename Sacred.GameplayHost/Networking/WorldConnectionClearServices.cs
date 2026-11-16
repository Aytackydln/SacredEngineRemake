namespace Sacred.GameplayHost.Networking;

/// <summary>Established connection memory, retained string extent, two node
/// rings and backend/diagnostic services used by original 0x7D4090. This does not
/// own or replace the existing transport reader/channel.</summary>
public sealed record WorldConnectionClearServices(WorldControlMemory Memory, Action<uint> ReleaseBackendSession,
    Action LogClosed, Action ClearRetainedText, WorldConnectionQueueClearServices FirstQueue,
    WorldConnectionQueueClearServices SecondQueue);
