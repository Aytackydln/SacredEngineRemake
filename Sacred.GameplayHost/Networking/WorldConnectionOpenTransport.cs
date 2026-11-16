namespace Sacred.GameplayHost.Networking;

/// <summary>TinCat factory/virtual service boundaries. Invoke preserves captured
/// handle, vtable offset and ordered UInt32 arguments; no new handle registry is
/// owned here. InitializeLogging owns the executable-derived ANSI log-channel
/// argument to virtual +0x50. Zero primary output is an unsupported fault path.</summary>
public sealed record WorldConnectionOpenTransport(Func<uint> Create, Func<uint> GetBuildNumber,
    Func<uint> CreateValues, Func<uint, uint, IReadOnlyList<uint>, uint> Invoke,
    Func<uint, bool> InitializeLogging);
