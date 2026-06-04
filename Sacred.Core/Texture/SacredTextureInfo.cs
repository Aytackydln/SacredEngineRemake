using SacredItemSimulator.GamePak;

namespace Sacred.Core.Texture;

public readonly record struct SacredTextureInfo(
    ulong TypeId,
    uint CompressedSize,
    long DataOffset,
    TextureImageInfo ImageInfo,
    SacredPakFile PakFile
);