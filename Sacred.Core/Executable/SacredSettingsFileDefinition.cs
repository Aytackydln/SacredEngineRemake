namespace Sacred.Core.Executable;

/// <summary>Original caller/loader data, independent of actual file contents.
/// Read capacities describe separate first/repeat fgets instructions.</summary>
public sealed class SacredSettingsFileDefinition(ReadOnlyMemory<byte> fileName, ReadOnlyMemory<byte> keyDelimiters,
    ReadOnlyMemory<byte> openMode, int firstReadCapacity, int nextReadCapacity,
    ReadOnlyMemory<byte> skipPrefixes, ReadOnlyMemory<byte> valueDelimiters, ReadOnlyMemory<byte> integerFormat)
{
    public ReadOnlyMemory<byte> FileName { get; } = fileName.ToArray();
    public ReadOnlyMemory<byte> KeyDelimiters { get; } = keyDelimiters.ToArray();
    public ReadOnlyMemory<byte> OpenMode { get; } = openMode.ToArray();
    public int FirstReadCapacity { get; } = firstReadCapacity;
    public int NextReadCapacity { get; } = nextReadCapacity;
    public ReadOnlyMemory<byte> SkipPrefixes { get; } = skipPrefixes.ToArray();
    public ReadOnlyMemory<byte> ValueDelimiters { get; } = valueDelimiters.ToArray();
    public ReadOnlyMemory<byte> IntegerFormat { get; } = integerFormat.ToArray();
}
