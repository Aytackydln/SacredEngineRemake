namespace Sacred.Core.Texture;

public readonly record struct TextureImageInfo(
    string FileName,
    ushort Width,
    ushort Height,
    byte RepeatedTypeId,
    uint RepeatedCompressedSize
);