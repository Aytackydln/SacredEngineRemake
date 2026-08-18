namespace Sacred.Core.Pak.Texture;

public readonly record struct SacredTextureInfo(
    ulong TypeId,
    uint CompressedSize,
    long DataOffset,
    TextureImageInfo ImageInfo,
    SacredPakFile PakFile
)
{
    public SacredTextureStorageFormat StorageFormat =>
        (SacredTextureStorageFormat)TypeId;
}
