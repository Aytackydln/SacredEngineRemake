using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Networking;

/// <summary>Raw native-handle platform boundary. Seek returns FFFFFFFF on error;
/// classification/path bytes/open arguments are determined by compared CRT code.</summary>
public sealed record WorldSettingsNativeFileServices(SacredCrtOpenServices Open,
    Func<uint, Memory<byte>, SacredCrtReadResult> Read,
    Func<uint, ReadOnlyMemory<byte>, SacredCrtWriteResult> Write,
    Func<uint, int, int, uint> Seek, Action Free);
