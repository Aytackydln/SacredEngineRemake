namespace Sacred.Core.Executable;

/// <summary>Original ANSI registry value. A null literal selects the directory
/// captured by the original GetCurrentDirectoryA call. Type is executable data.</summary>
public sealed class SacredLobbyRegistrationValue(ReadOnlyMemory<byte> name, uint type, ReadOnlyMemory<byte>? literal)
{
    public ReadOnlyMemory<byte> Name { get; } = name.ToArray();
    public uint Type { get; } = type;
    public ReadOnlyMemory<byte>? Literal { get; } = literal.HasValue
        ? (ReadOnlyMemory<byte>?)new ReadOnlyMemory<byte>(literal.Value.ToArray())
        : default(ReadOnlyMemory<byte>?);
}
