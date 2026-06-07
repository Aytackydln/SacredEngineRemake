namespace Sacred.Assets.Paks.Texture;

public readonly record struct TexturePakRecord(
    string Name,
    long Offset,
    int Size,
    ushort Width,
    ushort Height,
    byte Type);