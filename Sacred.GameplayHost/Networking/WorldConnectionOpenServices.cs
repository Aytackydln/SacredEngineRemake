namespace Sacred.GameplayHost.Networking;

/// <summary>Captured connection instance and caller-owned services for 0x7D2BD0.
/// Close/record/ring destruction, OS/environment inspection and optional backend
/// factory bodies remain explicit. InspectEnvironment owns original file/time
/// diagnostics. No actual OS, sockets, SDK or successful defaults are installed.</summary>
public sealed record WorldConnectionOpenServices(WorldControlMemory Memory, uint ConnectionAddress, Action Close,
    Action ClearPendingRecords, Action ClearFirstQueue, Action ClearSecondQueue,
    WorldConnectionOpenSockets Sockets, Func<uint> CreateMutex, Action InspectEnvironment,
    Action<WorldConnectionOpenLog, uint> Log, WorldConnectionOpenTransport Transport,
    Func<byte> ReadRuntimeFlag, Func<uint> CreateOptionalBackend,
    Action<uint> WriteOptionalBackend, Func<uint, bool> OptionalHasConnection);
