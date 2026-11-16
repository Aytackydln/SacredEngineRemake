namespace Sacred.GameplayHost.Runtime;

/// <summary>Platform/TLS/locks/heap boundaries for original fopen. PrepareUpdate
/// owns the separate read-write text tail/truncation path when reached.</summary>
public sealed record SacredCrtOpenServices(Func<int, bool> Allocate,
    Func<SacredCrtCreateRequest, uint> Create, Func<uint, uint> GetFileType,
    Func<uint, bool> Close, Func<uint> GetLastError, Func<SacredCrtDescriptor, bool> PrepareUpdate);
