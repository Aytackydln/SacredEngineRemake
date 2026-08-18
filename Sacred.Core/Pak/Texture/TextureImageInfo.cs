namespace Sacred.Core.Pak.Texture;

public readonly record struct TextureImageInfo(
    string FileName,
    ushort Width,
    ushort Height,
    byte RepeatedTypeId,
    uint RepeatedCompressedSize
)
{
    public SacredTextureStorageFormat StorageFormat =>
        (SacredTextureStorageFormat)RepeatedTypeId;
}
