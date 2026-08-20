namespace Sacred.Core;

public enum SacredPakFileType
{
    Weapon,
    Texture,
    Items,
    Unknown
}

public readonly record struct SacredPakFile(string FilePath, SacredPakFileType Type);
