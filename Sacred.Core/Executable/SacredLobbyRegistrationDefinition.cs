namespace Sacred.Core.Executable;

/// <summary>Executable-defined lobby registration request, independent of a
/// registry implementation. Original ANSI bytes remain lossless.</summary>
public sealed class SacredLobbyRegistrationDefinition(uint root, uint access, uint directoryCapacity,
    ReadOnlyMemory<byte> keyPath, IReadOnlyList<SacredLobbyRegistrationValue> values)
{
    public uint Root { get; } = root;
    public uint Access { get; } = access;
    public uint DirectoryCapacity { get; } = directoryCapacity;
    public ReadOnlyMemory<byte> KeyPath { get; } = keyPath.ToArray();
    public IReadOnlyList<SacredLobbyRegistrationValue> Values { get; } = Array.AsReadOnly(values.ToArray());
}
