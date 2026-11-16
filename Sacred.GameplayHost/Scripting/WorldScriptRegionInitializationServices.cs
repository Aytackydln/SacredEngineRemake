namespace Sacred.GameplayHost.Scripting;

/// <summary>Region initialization caller boundaries. TryInvoke reports service
/// delivery, not the script function's ignored native boolean return.</summary>
public sealed record WorldScriptRegionInitializationServices(Func<uint, byte> ReadSectorRegion,
    Func<byte, ushort> MapScriptRegion, Func<byte, uint> ReadFlags, Action<byte, uint> SetFlags,
    Func<string, uint?> CachedFunction, Func<string, int> FindFunction, Func<ushort, bool> TryInvoke);
