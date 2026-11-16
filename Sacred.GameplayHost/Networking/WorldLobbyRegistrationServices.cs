namespace Sacred.GameplayHost.Networking;

/// <summary>Caller-owned ANSI platform boundary. Directory returns captured
/// buffer bytes, not a Unicode-normalized path. CreateKey returns raw status and
/// handle. SetValue receives the original type and NUL-inclusive owned bytes.
/// Key path/value-name memories exclude their terminator; a native adapter owns
/// that ABI termination separately from the already-terminated value payload.
/// No installed registry or OS adapter is supplied.</summary>
public sealed record WorldLobbyRegistrationServices(Func<uint, ReadOnlyMemory<byte>> ReadDirectory,
    Func<uint, ReadOnlyMemory<byte>, uint, (int Status, uint Handle)> CreateKey,
    Action<uint, ReadOnlyMemory<byte>, uint, ReadOnlyMemory<byte>> SetValue, Action<uint> CloseKey);
