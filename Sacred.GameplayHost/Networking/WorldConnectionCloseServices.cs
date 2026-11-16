namespace Sacred.GameplayHost.Networking;

/// <summary>Captured connection and synchronous dependencies of native close.
/// ClearConnection can use the compared WorldConnectionClear service. The second
/// final queue clear owns native node erasure (not the first queue's node-only
/// free). Platform/SDK/storage ownership remains with the caller.</summary>
public sealed record WorldConnectionCloseServices(WorldControlMemory Memory,
    Func<uint> ReadOptionalBackend, Action<uint> DestroyOptionalBackend, Action<uint> WriteOptionalBackend,
    Action ClearConnection, Action<uint> CloseHandle, Action<uint> ShutdownSocket,
    Action<uint> CloseSocket, Action CleanupSockets, Action<uint, uint> InvokeTransport,
    Action ClearFirstQueue, Action ClearSecondQueue, Action ClearPendingRecords, Action LogClosed);
