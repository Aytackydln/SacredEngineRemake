namespace Sacred.Core.Executable;

/// <summary>Owned original getter operands. A null write default means no missing
/// value write. Empty-return text and write-default text are independent.</summary>
public sealed class SacredStringSettingDefinition(ReadOnlyMemory<byte> readKey, ReadOnlyMemory<byte> defaultKey,
    ReadOnlyMemory<byte>? defaultWriteValue, ReadOnlyMemory<byte>? emptyReturnValue, SacredCdKeySettingDefinition? cdKey)
{
    public ReadOnlyMemory<byte> ReadKey { get; } = readKey.ToArray();
    public ReadOnlyMemory<byte> DefaultKey { get; } = defaultKey.ToArray();
    public ReadOnlyMemory<byte>? DefaultWriteValue { get; } = defaultWriteValue.HasValue ? (ReadOnlyMemory<byte>?)defaultWriteValue.Value.ToArray() : null;
    public ReadOnlyMemory<byte>? EmptyReturnValue { get; } = emptyReturnValue.HasValue ? (ReadOnlyMemory<byte>?)emptyReturnValue.Value.ToArray() : null;
    public SacredCdKeySettingDefinition? CdKey { get; } = cdKey;
}
