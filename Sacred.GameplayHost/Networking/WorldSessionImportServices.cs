namespace Sacred.GameplayHost.Networking;

/// <summary>Complete importer boundaries. DecodeAnsi returning null denotes failed
/// native conversion (destination preserved). Current connection and ASE flag are
/// reread at native boundaries; no replacement connection or actor is created.</summary>
public sealed record WorldSessionImportServices(WorldControlMemory Controller, WorldSessionStringStorage Strings,
    Action<ReadOnlyMemory<byte>> RemoveValue, Func<WorldSessionSetting, ReadOnlyMemory<byte>> ReadString,
    Func<WorldSessionSetting, uint> ReadInteger, Func<ReadOnlyMemory<byte>, ReadOnlyMemory<char>?> DecodeAnsi,
    Func<ReadOnlyMemory<char>, ReadOnlyMemory<byte>> EncodeAnsi, WorldControlMemory AddressManager,
    Func<ReadOnlyMemory<byte>, uint> ParseAddress, Action<uint, ReadOnlyMemory<byte>> WriteAddressString,
    Func<WorldControlMemory?> ReadCurrentConnection, Func<byte> ReadAseFlag, Action<byte> WriteAseFlag,
    Action<string, ReadOnlyMemory<byte>, uint?> Log);
