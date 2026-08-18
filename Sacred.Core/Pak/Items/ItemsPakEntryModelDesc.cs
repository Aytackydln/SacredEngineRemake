using System.Runtime.InteropServices;
using System.Text;

namespace Sacred.Core.Pak.Items;

public readonly record struct ItemsPakEntryModelDesc(
    ItemsPakEntryInfo EntryInfo,
    ushort SomeShort2, // 2 bytes at offset 9, purpose unknown
    uint GraphicRenderFlags, // 4 bytes at offset 0
    uint TextureId, // 4 bytes at offset 8; item-specific texture.pak descriptor index for shared item models
    uint MixedBaseGroupId, // 4 bytes at offset 16; base id into mixed.pak static sprite groups
    uint ItemId, // 4 bytes at offset 32
    ushort StaticSpriteFrameCount, // 2 bytes at offset 44; consecutive mixed.pak groups used by animated static sprites
    byte RenderClass, // 1 byte at offset 46; affects static object draw ordering
    ushort ModelTransformFlags, // 2 bytes at offset 48; low byte is the frame duration in 10 ms units for animated static sprites
    ushort ModelExtent, // 2 bytes at offset 50; visual extent, including SimpleLight halo sizes 220/300/450
    string ModelName, // null-terminated string at 55, max length 32 bytes (including null terminator)
    uint EffectTextureId, // 4 bytes at offset 102; texture.pak descriptor index for model effect/fill/animated texture
    ushort Unknown112 // 2 bytes at offset 112; not the animated-texture speed
)
{
    public const uint UnlitGraphicFlag = 0x00020000;
    public const uint LowRenderClassMask = 0x0000000F;
    public const uint WorldLightRenderClass = 0x00000009;

    private static readonly Encoding SacredEncoding = Encoding.GetEncoding("iso-8859-1");

    public static IEnumerable<ItemsPakEntryModelDesc> ReadMany(
        SacredPakFile pakFile,
        byte[] pakBytes,
        IReadOnlyList<ItemsPakEntryInfo> entryInfos
    )
    {
        if (entryInfos.Count == 0)
            yield break;

        var firstOffset = entryInfos.Min(static entryInfo => entryInfo.ModelDescOffset);
        var layouts = ReadLayouts(pakBytes, entryInfos, firstOffset);

        foreach (var entryInfo in entryInfos)
        {
            var offsetDelta = entryInfo.ModelDescOffset - firstOffset;
            if (offsetDelta % ItemsPakEntryModelDescLayout.Size != 0)
                throw new InvalidDataException($"Items pak model descriptor offset {entryInfo.ModelDescOffset} is not aligned to {ItemsPakEntryModelDescLayout.Size} bytes.");

            var layoutIndex = checked((int)(offsetDelta / ItemsPakEntryModelDescLayout.Size));
            if ((uint)layoutIndex >= (uint)layouts.Length)
                throw new InvalidDataException($"Items pak model descriptor offset {entryInfo.ModelDescOffset} is outside the marshalled descriptor table.");

            yield return FromLayout(entryInfo, layouts[layoutIndex]);
        }
    }

    private static ItemsPakEntryModelDescLayout[] ReadLayouts(
        byte[] pakBytes,
        IReadOnlyList<ItemsPakEntryInfo> entryInfos,
        uint firstOffset
    )
    {
        var lastOffset = entryInfos.Max(static entryInfo => entryInfo.ModelDescOffset);
        var descriptorByteLength = checked((int)(lastOffset - firstOffset) + ItemsPakEntryModelDescLayout.Size);

        if (firstOffset > int.MaxValue || firstOffset + descriptorByteLength > pakBytes.Length)
            throw new InvalidDataException("Items pak model descriptor table is outside the file bounds.");

        return MemoryMarshal
            .Cast<byte, ItemsPakEntryModelDescLayout>(pakBytes.AsSpan((int)firstOffset, descriptorByteLength))
            .ToArray();
    }

    private static ItemsPakEntryModelDesc FromLayout(ItemsPakEntryInfo entryInfo,
        ItemsPakEntryModelDescLayout layout
    )
    {
        ReadOnlySpan<byte> modelNameBytes = layout.ModelNameBytes;

        return new ItemsPakEntryModelDesc(EntryInfo: entryInfo,
            SomeShort2: layout.SomeShort2,
            GraphicRenderFlags: layout.GraphicRenderFlags,
            TextureId: layout.TextureId,
            MixedBaseGroupId: layout.MixedBaseGroupId,
            ItemId: layout.ItemId,
            StaticSpriteFrameCount: layout.StaticSpriteFrameCount,
            RenderClass: layout.RenderClass,
            ModelTransformFlags: layout.ModelTransformFlags,
            ModelExtent: layout.ModelExtent,
            ModelName: ReadLocationString(modelNameBytes),
            EffectTextureId: layout.EffectTextureId,
            Unknown112: layout.Unknown112
        );
    }

    private static string ReadLocationString(ReadOnlySpan<byte> stringBytes)
    {
        var nullIndex = stringBytes.IndexOf((byte)0);
        if (nullIndex < 0)
            nullIndex = stringBytes.Length;

        return SacredEncoding.GetString(stringBytes[..nullIndex]);
    }

    /// <summary>
    /// Items.pak low render class 9. A texture-free, unlit entry is an authored
    /// light-volume marker when paired with a positive extent. This render
    /// class is also shared by ordinary mixed world sprites, so it is not an
    /// emitter discriminator by itself.
    /// </summary>
    public bool UsesWorldLightRenderClass =>
        (GraphicRenderFlags & LowRenderClassMask) == WorldLightRenderClass;
}
