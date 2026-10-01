
namespace Sacred.Assets.Paks.Texture;

public static class TexturePakDecoder
{
    public const int HeaderSize = 0x100;
    public const int DescriptorSize = 0x0C;
    public const int TextureHeaderSize = 0x50;

    public static TextureAsset Decode(TexturePakRecord record, ReadOnlySpan<byte> payload) =>
        TgaTextureDecoder.Decode(record, payload);

    public static int ReadEntryCount(uint entryCount, long archiveLength)
    {
        var maxDescriptorCount = Math.Max(0, (archiveLength - HeaderSize) / DescriptorSize);

        if (entryCount > int.MaxValue || entryCount > maxDescriptorCount)
            throw new InvalidDataException(
                $"Texture.pak entry count {entryCount} exceeds the descriptor capacity {maxDescriptorCount}.");

        return (int)entryCount;
    }

}
