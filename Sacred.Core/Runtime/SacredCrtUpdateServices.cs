namespace Sacred.Core.Runtime;

/// <summary>Raw Win32 effects for the text update-open tail. Truncate acts at
/// the current OS position, as SetEndOfFile does.</summary>
public sealed record SacredCrtUpdateServices(Func<int, int, uint> Seek,
    Func<Memory<byte>, SacredCrtReadResult> Read, Func<bool> Truncate, Func<uint> GetLastError);
