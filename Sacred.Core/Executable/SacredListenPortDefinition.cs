namespace Sacred.Core.Executable;

/// <summary>Independent original getter operands. Write-default and return-default
/// literals are retained separately; default CRT classes do not prove current locale.</summary>
public sealed class SacredListenPortDefinition(ReadOnlyMemory<byte> readKey, ReadOnlyMemory<byte> defaultKey,
    uint defaultWriteValue, uint defaultReturnValue, ReadOnlyMemory<ushort> defaultClasses)
{
    public ReadOnlyMemory<byte> ReadKey { get; } = readKey.ToArray();
    public ReadOnlyMemory<byte> DefaultKey { get; } = defaultKey.ToArray();
    public uint DefaultWriteValue { get; } = defaultWriteValue;
    public uint DefaultReturnValue { get; } = defaultReturnValue;
    public ReadOnlyMemory<ushort> DefaultClasses { get; } = defaultClasses.ToArray();
}
