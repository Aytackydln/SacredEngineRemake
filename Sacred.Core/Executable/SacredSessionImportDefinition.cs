namespace Sacred.Core.Executable;

/// <summary>ANSI literals referenced by the original settings importer. Values
/// exclude terminating NUL; settings getters and their defaults are separate.</summary>
public sealed class SacredSessionImportDefinition(ReadOnlyMemory<byte> sessionDataKey,
    ReadOnlyMemory<byte> characterKey, ReadOnlyMemory<byte> maskedSuffix)
{
    public ReadOnlyMemory<byte> SessionDataKey { get; } = sessionDataKey.ToArray();
    public ReadOnlyMemory<byte> CharacterKey { get; } = characterKey.ToArray();
    public ReadOnlyMemory<byte> MaskedSuffix { get; } = maskedSuffix.ToArray();
}
