namespace Sacred.Core.Executable;

/// <summary>Operands reached by the importer's zero/zero CD-key getter arguments.
/// Other formatting/validation arguments require their own compared paths.</summary>
public sealed class SacredCdKeySettingDefinition(ReadOnlyMemory<byte> separator, uint separatedLength)
{
    public ReadOnlyMemory<byte> Separator { get; } = separator.ToArray();
    public uint SeparatorBypassLength { get; } = separatedLength;
}
