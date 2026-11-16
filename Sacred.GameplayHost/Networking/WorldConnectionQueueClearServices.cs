namespace Sacred.GameplayHost.Networking;

/// <summary>Caller-owned valid circular node storage. Free releases only a
/// 12-byte node, not its payload. Native traversal rereads the current head after
/// freeing each captured node; the allocator callback may change storage.</summary>
public sealed record WorldConnectionQueueClearServices(Func<uint> ReadHead, Func<uint, uint> ReadNext,
    Action<uint, uint> WriteNext, Action<uint, uint> WritePrevious, Action<uint> FreeNode,
    Action Enter, Action Exit);
