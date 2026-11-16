namespace Sacred.Core.Executable;

/// <summary>Independent original numeric getter operands. A null default-write
/// value means the missing path returns without modifying the settings map.</summary>
public sealed class SacredIntegerSettingDefinition(ReadOnlyMemory<byte> readKey,
    ReadOnlyMemory<byte> defaultKey, uint? defaultWriteValue, uint defaultReturnValue)
{
    public ReadOnlyMemory<byte> ReadKey { get; } = readKey.ToArray();
    public ReadOnlyMemory<byte> DefaultKey { get; } = defaultKey.ToArray();
    public uint? DefaultWriteValue { get; } = defaultWriteValue;
    public uint DefaultReturnValue { get; } = defaultReturnValue;
}
