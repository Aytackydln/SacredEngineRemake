namespace Sacred.Core.Executable;

public sealed class SacredSettingsSaveDefinition(ReadOnlyMemory<byte> openMode, ReadOnlyMemory<byte> recordFormat)
{
    public ReadOnlyMemory<byte> OpenMode { get; } = openMode.ToArray();
    public ReadOnlyMemory<byte> RecordFormat { get; } = recordFormat.ToArray();
}
