namespace SacredItemSimulator.GamePak;

public enum SacredPakFileType
{
    Weapon,
    Texture,
    Items,
    Unknown
}

public readonly record struct SacredPakFile
{
    public SacredPakFile(string filePath, SacredPakFileType type)
    {
        FilePath = filePath;
        Type = type;
    }

    public string FilePath { get; }
    public SacredPakFileType Type { get; }
}