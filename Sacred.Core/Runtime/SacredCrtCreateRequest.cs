namespace Sacred.Core.Runtime;

/// <summary>CreateFileA arguments derived by the original _sopen path.</summary>
public sealed record SacredCrtCreateRequest(ReadOnlyMemory<byte> Path, uint DesiredAccess,
    uint ShareMode, bool InheritHandle, uint CreationDisposition, uint FlagsAndAttributes);
