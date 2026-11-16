namespace Sacred.GameplayHost.Networking;

/// <summary>Explicit native backend, storage, synchronization and transport boundaries.
/// PendingCount is a signed logical extent, not dictionary count; ReadPending must
/// provide established backing if a replacement has a shorter logical range.
/// Poll, locks, allocation/append and query remain caller-owned services.</summary>
public sealed record WorldConnectionBackendPumpServices<TBackend>(
    Func<TBackend?> ReadBackend, Action<TBackend> Poll,
    Action<TBackend> EnterBackend, Action<TBackend> ExitBackend,
    Func<TBackend, int> PendingCount, Func<TBackend, int, uint> ReadPending,
    Action<TBackend> ClearPending,
    Action EnterConnection, Action ExitConnection, Action<uint> AppendConnection,
    Func<TBackend, bool> Query) where TBackend : class;
