using System.Runtime.InteropServices;
using System.Text;

namespace Sacred.Core.Pak.Items;

public readonly record struct ItemsPakEntryModelDesc(
    ItemsPakEntryInfo EntryInfo,
    ushort SomeShort2, // 2 bytes at offset 9, purpose unknown
    uint GraphicRenderFlags, // 4 bytes at offset 0
    uint MiniObjectTextureId, // 4 bytes at offset 4; texture.pak descriptor index for Static.pak atlas sprites
    uint TextureId, // 4 bytes at offset 8; item-specific texture.pak descriptor index for shared item models
    uint MixedBaseGroupId, // 4 bytes at offset 16; base id into mixed.pak static sprite groups
    uint ItemId, // 4 bytes at offset 32
    ushort StaticSpriteFrameCount, // 2 bytes at offset 44; consecutive mixed.pak groups used by animated static sprites
    byte RenderClass, // 1 byte at offset 46; affects static object draw ordering
    ushort ModelTransformFlags, // 2 bytes at offset 48; low byte is the frame duration in 10 ms units for animated static sprites
    ushort ModelExtent, // 2 bytes at offset 50; authored visual or illumination extent
    string ModelName, // null-terminated string at 55, max length 32 bytes (including null terminator)
    uint EffectTextureId, // 4 bytes at offset 102; texture.pak descriptor index for model effect/fill/animated texture
    ushort Unknown112 // 2 bytes at offset 112; not the animated-texture speed
)
{
    public const uint LightEmittingGraphicFlag = 0x00020000;
    public const uint LowRenderClassMask = 0x0000000F;
    public const uint AnimatedMiniObjectRenderClass = 0x00000008;
    public const uint MixedSpriteOrLightMarkerRenderClass = 0x00000009;
    public const uint StaticMiniObjectRenderClass = 0x0000000C;

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
            MiniObjectTextureId: layout.MiniObjectTextureId,
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

    public uint LowRenderClass => GraphicRenderFlags & LowRenderClassMask;

    /// <summary>
    /// Marks records that author a light-emitting effect or illumination extent.
    /// This does not make the record's visible sprite exempt from world lighting.
    /// </summary>
    public bool IsLightEmitting => (GraphicRenderFlags & LightEmittingGraphicFlag) != 0;

    public bool UsesAnimatedMiniObjectRenderClass =>
        LowRenderClass == AnimatedMiniObjectRenderClass;

    public bool UsesStaticMiniObjectRenderClass =>
        LowRenderClass == StaticMiniObjectRenderClass;

    public bool UsesMixedSpriteOrLightMarkerRenderClass =>
        LowRenderClass == MixedSpriteOrLightMarkerRenderClass;

    /// <summary>True when offset 0x04 supplies the atlas selected by Static.pak.</summary>
    public bool UsesMiniObjectTexture =>
        MiniObjectTextureId != 0 &&
        MixedBaseGroupId == 0 &&
        TextureId == 0 &&
        EffectTextureId == 0 &&
        (UsesAnimatedMiniObjectRenderClass || UsesStaticMiniObjectRenderClass);

    /// <summary>
    /// An animated, light-emitting mini-object with an authored extent emits the visible
    /// sprite-centred halo used by candles, lanterns, and related effects.
    /// </summary>
    public bool EmitsAnimatedSpriteHalo =>
        UsesMiniObjectTexture &&
        UsesAnimatedMiniObjectRenderClass &&
        IsLightEmitting &&
        ModelExtent > 0;

    /// <summary>
    /// Texture-free class-9 records with an extent are authored illumination
    /// volumes. They are distinct from visible sprite halos.
    /// </summary>
    public bool IsWorldLightMarker =>
        UsesMixedSpriteOrLightMarkerRenderClass &&
        IsLightEmitting &&
        ModelExtent > 0 &&
        MiniObjectTextureId == 0 &&
        MixedBaseGroupId == 0 &&
        TextureId == 0 &&
        EffectTextureId == 0;

    /// <summary>
    /// Mixed class-9 sprites with the observed zero-high-bit signature are candidates
    /// for per-pixel emissive treatment. Pixel data remains the final selector.
    /// </summary>
    public bool MayContainMixedSpriteEmission =>
        GraphicRenderFlags == MixedSpriteOrLightMarkerRenderClass &&
        MixedBaseGroupId != 0 &&
        MiniObjectTextureId == 0 &&
        TextureId == 0 &&
        EffectTextureId == 0 &&
        StaticSpriteFrameCount == 0 &&
        ModelTransformFlags == 0x0100 &&
        ModelExtent == 0;
}
